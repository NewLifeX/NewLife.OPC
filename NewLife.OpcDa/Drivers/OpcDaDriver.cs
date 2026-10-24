using System.ComponentModel;
using NewLife.IoT;
using NewLife.IoT.Drivers;
using NewLife.IoT.ThingModels;
using NewLife.Serialization;
using Opc;
using Opc.Da;
using Node = NewLife.IoT.Drivers.Node;

namespace NewLife.OPC.Drivers;

/// <summary>
/// OpcDa协议
/// </summary>
[Driver("OpcDa")]
[DisplayName("OpcDa协议")]
public class OpcDaDriver : DriverBase, IDriver
{
    private Opc.Da.Server _client;
    private Int32 _nodes;

    #region 构造
    /// <summary>
    /// 销毁时，关闭连接
    /// </summary>
    /// <param name="disposing"></param>
    protected override void Dispose(Boolean disposing)
    {
        base.Dispose(disposing);

        _client.TryDispose();
        _client = null;
    }
    #endregion

    #region 方法
    /// <summary>
    /// 创建驱动参数对象，可序列化成Xml/Json作为该协议的参数模板
    /// </summary>
    /// <returns></returns>
    protected override IDriverParameter OnCreateParameter() => new OpcDaParameter();

    /// <summary>
    /// 打开通道。一个OPC设备可能分为多个通道读取，需要共用Tcp连接，以不同节点区分
    /// </summary>
    /// <param name="channel">通道</param>
    /// <param name="parameter">参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    public override Task<INode> OpenAsync(IDevice channel, IDriverParameter parameter, CancellationToken cancellationToken = default)
    {
        var pm = parameter as OpcDaParameter;
        if (pm == null) throw new ArgumentNullException(nameof(parameter));

        if (_client == null)
        {
            var uri = new Uri(pm.Address);
            var url = new URL(uri.AbsolutePath)
            {
                Scheme = uri.Scheme,
                HostName = uri.Host
            };

            var client = new Opc.Da.Server(new OpcCom.Factory(), url);
            client.Connect();

            _client = client;
        }

        Interlocked.Increment(ref _nodes);

        INode node = new Node
        {
            Driver = this,
            Device = channel,
            Parameter = pm,
        };

        return Task.FromResult(node);
    }

    /// <summary>
    /// 关闭设备驱动
    /// </summary>
    /// <param name="node">节点对象</param>
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
    /// <param name="points">点位集合，Address属性地址示例：D100、C100、W100、H100</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>读取结果，包含点位数据、质量码和诊断帧</returns>
    public override Task<ReadResult> ReadAsync(INode node, IPoint[] points, CancellationToken cancellationToken = default)
    {
        if (points == null || points.Length == 0) return Task.FromResult(ReadResult.Success([], []));
        if (_client == null) return Task.FromResult(ReadResult.Fail(IoTErrorCode.ConnectionFailed, "OPC DA server is not open.", DataQuality.BadNotConnected));

        cancellationToken.ThrowIfCancellationRequested();

        // 构造参数，准备批量读取
        var list = new List<IPoint>();
        var ids = new List<Item>();
        foreach (var point in points)
        {
            if (point.Address.IsNullOrEmpty()) continue;

            list.Add(point);
            ids.Add(new Item
            {
                ItemName = point.Address
            });
        }

        if (ids.Count == 0) return Task.FromResult(ReadResult.Success([], []));

        try
        {
            // 批量读取
            var results = _client.Read(ids.ToArray());

            // 仅收集读取成功的点位
            var goodPoints = new List<IPoint>();
            var values = new List<Object>();
            for (var i = 0; i < results.Length; i++)
            {
                var rs = results[i];
                if (rs != null && rs.Quality == Quality.Good)
                {
                    goodPoints.Add(list[i]);
                    values.Add(rs.Value);
                }
            }

            return Task.FromResult(ReadResult.Success(goodPoints.ToArray(), values.ToArray()));
        }
        catch (Exception ex) when (ex is Opc.ResultIDException or Opc.NotConnectedException or Opc.ConnectFailedException or Opc.AccessDeniedException or Opc.ServerTimeoutException or Opc.InvalidResponseException or System.Runtime.InteropServices.COMException)
        {
            Log?.Error(ex.ToString());

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
        if (_client == null) return Task.FromResult(WriteResult.Fail(IoTErrorCode.ConnectionFailed, "OPC DA server is not open."));

        cancellationToken.ThrowIfCancellationRequested();

        var values = new List<ItemValue>();
        foreach (var request in requests)
        {
            var point = request.Point;
            if (point == null || point.Address.IsNullOrEmpty()) continue;

            values.Add(new ItemValue
            {
                ItemName = point.Address,
                Value = request.Value
            });
        }

        if (values.Count == 0) return Task.FromResult(WriteResult.SuccessBatch(0));

        try
        {
            var results = _client.Write(values.ToArray());

            var count = 0;
            foreach (var result in results)
            {
                if (result?.ResultID != null && result.ResultID.Succeeded()) count++;
            }

            if (count == 0) return Task.FromResult(WriteResult.Fail(IoTErrorCode.WriteError, "Write failed"));
            if (count < values.Count) return Task.FromResult(new WriteResult { IsSuccess = true, AffectedCount = count });

            return Task.FromResult(WriteResult.SuccessBatch(count));
        }
        catch (Exception ex) when (ex is Opc.ResultIDException or Opc.NotConnectedException or Opc.ConnectFailedException or Opc.AccessDeniedException or Opc.ServerTimeoutException or Opc.InvalidResponseException or System.Runtime.InteropServices.COMException)
        {
            Log?.Error(ex.ToString());

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