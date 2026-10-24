using System.ComponentModel;
using NewLife.IoT;
using NewLife.IoT.Drivers;
using NewLife.IoT.ThingModels;
using Opc.Ua;
using Opc.Ua.Client;
using INode = NewLife.IoT.Drivers.INode;
using Node = NewLife.IoT.Drivers.Node;
using WriteRequest = NewLife.IoT.Drivers.WriteRequest;

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
    protected override IDriverParameter OnCreateParameter() => new OpcUaParameter();

    /// <summary>
    /// 打开通道。一个OPC设备可能分为多个通道读取，需要共用Tcp连接，以不同节点区分
    /// </summary>
    /// <param name="channel">通道</param>
    /// <param name="parameter">参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    public override async Task<INode> OpenAsync(IDevice channel, IDriverParameter parameter, CancellationToken cancellationToken = default)
    {
        var pm = parameter as OpcUaParameter;
        if (pm == null) throw new ArgumentNullException(nameof(parameter));

        if (_client == null)
        {
            var configuration = GetConfig();
            var endpointDescription = CoreClientUtils.SelectEndpoint(pm.Address, false);
            var endpointConfiguration = EndpointConfiguration.Create(configuration);

            // 匿名登录
            var userIdentity = new UserIdentity(new AnonymousIdentityToken());

            var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            var client = await Session.Create(
                configuration,
                endpoint,
                false,
                false,
                (String.IsNullOrEmpty(OpcUaName)) ? configuration.ApplicationName : OpcUaName,
                60000,
                userIdentity,
                new String[] { }).ConfigureAwait(false);

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
    /// <param name="cancellationToken">取消令牌</param>
    public override Task CloseAsync(INode node, CancellationToken cancellationToken = default)
    {
        if (node is Node impl) impl.IsConnected = false;

        if (Interlocked.Decrement(ref _nodes) <= 0)
        {
            _client.TryDispose();
            _client = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 读取数据
    /// </summary>
    /// <param name="node">节点对象，可存储站号等信息，仅驱动自己识别</param>
    /// <param name="points">点位集合，Address属性为OPC节点编号</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>读取结果，包含点位数据、质量码和诊断帧</returns>
    public override Task<ReadResult> ReadAsync(INode node, IPoint[] points, CancellationToken cancellationToken = default)
    {
        if (points == null || points.Length == 0) return Task.FromResult(ReadResult.Success([], []));
        if (_client == null) return Task.FromResult(ReadResult.Fail(IoTErrorCode.ConnectionFailed, "OPC UA session is not open.", DataQuality.BadNotConnected));

        cancellationToken.ThrowIfCancellationRequested();

        // 构造参数，准备批量读取
        var list = new List<IPoint>();
        var ids = new ReadValueIdCollection();
        foreach (var point in points)
        {
            if (point.Address.IsNullOrEmpty()) continue;

            list.Add(point);
            ids.Add(new ReadValueId
            {
                NodeId = new NodeId(point.Address),
                AttributeId = Attributes.Value
            });
        }

        if (ids.Count == 0) return Task.FromResult(ReadResult.Success([], []));

        try
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

            // 仅收集读取成功的点位
            var goodPoints = new List<IPoint>();
            var values = new List<Object>();
            for (var i = 0; i < results.Count; i++)
            {
                var rs = results[i];
                if (rs != null && DataValue.IsGood(rs))
                {
                    goodPoints.Add(list[i]);
                    values.Add(rs.Value);
                }
            }

            return Task.FromResult(ReadResult.Success(goodPoints.ToArray(), values.ToArray()));
        }
        catch (Exception ex) when (ex is ServiceResultException or TimeoutException)
        {
            return Task.FromResult(ReadResult.Fail(IoTErrorCode.ReadError, ex.Message));
        }
    }

    /// <summary>
    /// 批量写入数据
    /// </summary>
    /// <param name="node">节点对象，可存储站号等信息，仅驱动自己识别</param>
    /// <param name="requests">写入请求数组，每项含目标点位和值</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>写入结果，AffectedCount为实际成功点数</returns>
    public override Task<WriteResult> WriteAsync(INode node, WriteRequest[] requests, CancellationToken cancellationToken = default)
    {
        if (requests == null || requests.Length == 0) return Task.FromResult(WriteResult.SuccessBatch(0));
        if (_client == null) return Task.FromResult(WriteResult.Fail(IoTErrorCode.ConnectionFailed, "OPC UA session is not open."));

        cancellationToken.ThrowIfCancellationRequested();

        var valuesToWrite = new WriteValueCollection();
        foreach (var request in requests)
        {
            var point = request.Point;
            if (point == null || point.Address.IsNullOrEmpty()) continue;

            var valueToWrite = new WriteValue
            {
                NodeId = new NodeId(point.Address),
                AttributeId = Attributes.Value
            };
            valueToWrite.Value.Value = request.Value;
            valueToWrite.Value.StatusCode = StatusCodes.Good;
            valueToWrite.Value.ServerTimestamp = DateTime.MinValue;
            valueToWrite.Value.SourceTimestamp = DateTime.MinValue;

            valuesToWrite.Add(valueToWrite);
        }

        if (valuesToWrite.Count == 0) return Task.FromResult(WriteResult.SuccessBatch(0));

        try
        {
            // 写入当前的值
            _client.Write(
                null,
                valuesToWrite,
                out var results,
                out var diagnosticInfos);

            ClientBase.ValidateResponse(results, valuesToWrite);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToWrite);

            var count = 0;
            var firstError = StatusCodes.Good;
            for (var i = 0; i < results.Count; i++)
            {
                var status = results[i];
                if (StatusCode.IsBad(status))
                {
                    if (firstError == StatusCodes.Good) firstError = status.Code;
                }
                else
                    count++;
            }

            if (count == 0) return Task.FromResult(WriteResult.Fail(IoTErrorCode.WriteError, $"Write failed: {firstError}"));
            if (count < valuesToWrite.Count) return Task.FromResult(new WriteResult { IsSuccess = true, AffectedCount = count });

            return Task.FromResult(WriteResult.SuccessBatch(count));
        }
        catch (Exception ex) when (ex is ServiceResultException or TimeoutException)
        {
            return Task.FromResult(WriteResult.Fail(IoTErrorCode.WriteError, ex.Message));
        }
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