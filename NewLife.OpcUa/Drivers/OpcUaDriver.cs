using System.ComponentModel;
using NewLife.IoT;
using NewLife.IoT.Drivers;
using NewLife.IoT.ThingModels;
using Opc.Ua;
using Opc.Ua.Client;
using INode = NewLife.IoT.Drivers.INode;
using Node = NewLife.IoT.Drivers.Node;

namespace NewLife.OPC.Drivers;

/// <summary>
/// OpcUa协议
/// </summary>
[Driver("OpcUa")]
[DisplayName("OpcUa协议")]
public class OpcUaDriver : DriverBase
{
    private ISession _client;
    private Int32 _nodes;
    private SessionReconnectHandler _reConnectHandler;

    /// <summary>应用名</summary>
    public String OpcUaName { get; set; } = "NewLife.OpcUa";

    #region 构造
    ///// <summary>
    ///// 销毁时，关闭连接
    ///// </summary>
    ///// <param name="disposing"></param>
    //protected override void Dispose(Boolean disposing)
    //{
    //    base.Dispose(disposing);

    //    _client.TryDispose();
    //    _client = null;
    //}
    #endregion

    #region 方法
    /// <summary>
    /// 创建驱动参数对象，可序列化成Xml/Json作为该协议的参数模板
    /// </summary>
    /// <returns></returns>
    public override IDriverParameter GetDefaultParameter() => new OpcUaParameter();

    /// <summary>
    /// 打开通道。一个OPC设备可能分为多个通道读取，需要共用Tcp连接，以不同节点区分
    /// </summary>
    /// <param name="channel">通道</param>
    /// <param name="parameter">参数</param>
    /// <returns></returns>
    public override INode Open(IDevice channel, IDriverParameter parameter)
    {
        var pm = parameter as OpcUaParameter;

        if (_client == null)
        {
            var configuration = GetConfig();
            var endpointDescription = CoreClientUtils.SelectEndpoint(pm.Address, false);
            var endpointConfiguration = EndpointConfiguration.Create(configuration);

            // 匿名登录
            var userIdentity = new UserIdentity(new AnonymousIdentityToken());

            var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            var client = Session.Create(
                configuration,
                endpoint,
                false,
                false,
                (String.IsNullOrEmpty(OpcUaName)) ? configuration.ApplicationName : OpcUaName,
                60000,
                userIdentity,
                new String[] { }).Result;

            // set up keep alive callback.
            client.KeepAlive += Session_KeepAlive;

            _client = client;
        }

        Interlocked.Increment(ref _nodes);

        return new Node
        {
            Driver = this,
            Device = channel,
            Parameter = pm,
        };
    }

    private ApplicationConfiguration GetConfig()
    {

        var certificateValidator = new CertificateValidator();
        certificateValidator.CertificateValidation += (sender, eventArgs) =>
        {
            if (ServiceResult.IsGood(eventArgs.Error))
                eventArgs.Accept = true;
            else if (eventArgs.Error.StatusCode.Code == StatusCodes.BadCertificateUntrusted)
                eventArgs.Accept = true;
            else
                throw new Exception(String.Format("Failed to validate certificate with error code {0}: {1}", eventArgs.Error.Code, eventArgs.Error.AdditionalInfo));
        };

        var securityConfigurationcv = new SecurityConfiguration
        {
            AutoAcceptUntrustedCertificates = true,
            RejectSHA1SignedCertificates = false,
            MinimumCertificateKeySize = 1024,
        };
        certificateValidator.Update(securityConfigurationcv);

        // Build the application configuration
        var configuration = new ApplicationConfiguration
        {
            ApplicationName = OpcUaName,
            ApplicationType = ApplicationType.Client,
            CertificateValidator = certificateValidator,
            ApplicationUri = "urn:MyClient", //Kepp this syntax
            ProductUri = "OpcUaClient",

            ServerConfiguration = new ServerConfiguration
            {
                MaxSubscriptionCount = 100000,
                MaxMessageQueueSize = 1000000,
                MaxNotificationQueueSize = 1000000,
                MaxPublishRequestCount = 10000000,
            },

            SecurityConfiguration = new SecurityConfiguration
            {
                AutoAcceptUntrustedCertificates = true,
                RejectSHA1SignedCertificates = false,
                MinimumCertificateKeySize = 1024,
                SuppressNonceValidationErrors = true,

                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.X509Store,
                    StorePath = "CurrentUser\\My",
                    SubjectName = OpcUaName,
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.X509Store,
                    StorePath = "CurrentUser\\Root",
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.X509Store,
                    StorePath = "CurrentUser\\Root",
                }
            },

            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 6000000,
                MaxStringLength = Int32.MaxValue,
                MaxByteStringLength = Int32.MaxValue,
                MaxArrayLength = 65535,
                MaxMessageSize = 419430400,
                MaxBufferSize = 65535,
                ChannelLifetime = -1,
                SecurityTokenLifetime = -1
            },
            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = -1,
                MinSubscriptionLifetime = -1,
            },
            DisableHiResClock = true
        };

        configuration.Validate(ApplicationType.Client);

        return configuration;
    }

    private void Session_KeepAlive(ISession session, KeepAliveEventArgs e)
    {
        // check for events from discarded sessions.
        if (!Object.ReferenceEquals(session, _client)) return;

        // start reconnect sequence on communication error.
        if (ServiceResult.IsBad(e.Status))
        {
            WriteLog("Reconnecting");

            if (_reConnectHandler == null)
            {
                _reConnectHandler = new SessionReconnectHandler();
                _reConnectHandler.BeginReconnect(_client, 10 * 1000, Server_ReconnectComplete);
            }

            return;
        }

        WriteLog("Connected [{0}]", session.Endpoint.EndpointUrl);
    }

    private void Server_ReconnectComplete(Object sender, EventArgs e)
    {
        // ignore callbacks from discarded objects.
        if (!Object.ReferenceEquals(sender, _reConnectHandler)) return;

        _client = _reConnectHandler.Session;
        _reConnectHandler.Dispose();
        _reConnectHandler = null;
    }

    /// <summary>
    /// 关闭设备驱动
    /// </summary>
    /// <param name="node"></param>
    public override void Close(INode node)
    {
        if (Interlocked.Decrement(ref _nodes) <= 0)
        {
            _client.TryDispose();
            _client = null;
        }
    }

    /// <summary>
    /// 读取数据
    /// </summary>
    /// <param name="node">节点对象，可存储站号等信息，仅驱动自己识别</param>
    /// <param name="points">点位集合，Address属性地址示例：D100、C100、W100、H100</param>
    /// <returns></returns>
    public override IDictionary<String, Object> Read(INode node, IPoint[] points)
    {
        var dic = new Dictionary<String, Object>();

        if (points == null || points.Length == 0) return dic;
        points = points.Where(e => !e.Address.IsNullOrEmpty()).ToArray();

        // 构造参数，准备批量读取
        var ids = new ReadValueIdCollection();
        foreach (var point in points)
        {
            var id = new ReadValueId
            {
                NodeId = new NodeId(point.Address),
                AttributeId = Attributes.Value
            };
            ids.Add(id);
        }

        if (ids.Count > 0)
        {
            // 批量读取
            _client.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                ids,
                out var results,
                out var diagnosticInfos);

            ClientBase.ValidateResponse(results, ids);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, ids);

            // 按照点位逐个赋值
            for (var i = 0; i < results.Count; i++)
            {
                var rs = results[i];
                if (rs != null && DataValue.IsGood(rs))
                {
                    dic[points[i].Name] = rs.Value;
                }
            }
        }

        return dic;
    }

    /// <summary>
    /// 写入数据
    /// </summary>
    /// <param name="node">节点对象，可存储站号等信息，仅驱动自己识别</param>
    /// <param name="point">点位</param>
    /// <param name="value">数值</param>
    public override Object Write(INode node, IPoint point, Object value)
    {
        var valueToWrite = new WriteValue()
        {
            NodeId = new NodeId(point.Address),
            AttributeId = Attributes.Value
        };
        valueToWrite.Value.Value = value;
        valueToWrite.Value.StatusCode = StatusCodes.Good;
        valueToWrite.Value.ServerTimestamp = DateTime.MinValue;
        valueToWrite.Value.SourceTimestamp = DateTime.MinValue;

        var valuesToWrite = new WriteValueCollection { valueToWrite };

        // 写入当前的值

        _client.Write(
            null,
            valuesToWrite,
            out var results,
            out var diagnosticInfos);

        ClientBase.ValidateResponse(results, valuesToWrite);
        ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToWrite);

        if (StatusCode.IsBad(results[0])) throw new ServiceResultException(results[0]);

        return !StatusCode.IsBad(results[0]);
    }

    ///// <summary>
    ///// 控制设备，特殊功能使用
    ///// </summary>
    ///// <param name="node"></param>
    ///// <param name="parameters"></param>
    ///// <exception cref="NotImplementedException"></exception>
    //public virtual void Control(INode node, IDictionary<String, Object> parameters) => throw new NotImplementedException();
    #endregion
}