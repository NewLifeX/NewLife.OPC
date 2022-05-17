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
public class OpcDaDriver : DisposeBase, IDriver
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
    public virtual IDriverParameter CreateParameter() => new OpcDaParameter();

    /// <summary>
    /// 打开通道。一个OPC设备可能分为多个通道读取，需要共用Tcp连接，以不同节点区分
    /// </summary>
    /// <param name="channel">通道</param>
    /// <param name="parameters">参数</param>
    /// <returns></returns>
    public INode Open(IDevice channel, IDictionary<String, Object> parameters)
    {
        var pm = JsonHelper.Convert<OpcDaParameter>(parameters);

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

        return new Node
        {
            Driver = this,
            Device = channel,
            Parameter = pm,
        };
    }

    /// <summary>
    /// 关闭设备驱动
    /// </summary>
    /// <param name="node"></param>
    public void Close(INode node)
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
    public IDictionary<String, Object> Read(INode node, IPoint[] points)
    {
        var dic = new Dictionary<String, Object>();

        if (points == null || points.Length == 0) return dic;
        points = points.Where(e => !e.Address.IsNullOrEmpty()).ToArray();

        // 构造参数，准备批量读取
        var ids = new List<Item>();
        foreach (var point in points)
        {
            var id = new Item
            {
                ItemName = point.Address
            };
            ids.Add(id);
        }

        if (ids.Count > 0)
        {
            // 批量读取
            var results = _client.Read(ids.ToArray());

            // 按照点位逐个赋值
            for (var i = 0; i < results.Length; i++)
            {
                var rs = results[i];
                if (rs != null && rs.Quality == Quality.Good)
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
    public Object Write(INode node, IPoint point, Object value)
    {
        var val = new ItemValue
        {
            ItemName = point.Address,
            Value = value
        };

        var result = _client.Write(new[] { val });

        return result;
    }

    /// <summary>
    /// 控制设备，特殊功能使用
    /// </summary>
    /// <param name="node"></param>
    /// <param name="parameters"></param>
    /// <exception cref="NotImplementedException"></exception>
    public virtual void Control(INode node, IDictionary<String, Object> parameters) => throw new NotImplementedException();
    #endregion
}