﻿using System.ComponentModel;
using Hylasoft.Opc.Da;
using NewLife.IoT;
using NewLife.IoT.Drivers;
using NewLife.IoT.ThingModels;
using NewLife.Serialization;

namespace NewLife.OPC.Drivers;

/// <summary>
/// 设备网络心跳驱动
/// </summary>
/// <remarks>
/// IoT驱动，通过Ping探测到目标设备的网络情况，并收集延迟数据
/// </remarks>
[Driver("OPC")]
[DisplayName("OPC协议")]
public class OPCDriver : DisposeBase, IDriver
{
    private DaClient _client;
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
    public virtual IDriverParameter CreateParameter() => new OPCParameter();

    /// <summary>
    /// 打开通道。一个OPC设备可能分为多个通道读取，需要共用Tcp连接，以不同节点区分
    /// </summary>
    /// <param name="channel">通道</param>
    /// <param name="parameters">参数</param>
    /// <returns></returns>
    public INode Open(IDevice channel, IDictionary<String, Object> parameters)
    {
        var pm = JsonHelper.Convert<OPCParameter>(parameters);

        if (_client == null)
        {
            var client = new DaClient(new Uri(pm.Address));
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

        var p = node.Parameter as OPCParameter;
        foreach (var point in points)
        {
            if (!point.Address.IsNullOrEmpty())
            {
                var rs = _client.Read<Int32>(point.Address);

                if (rs.Quality == Hylasoft.Opc.Common.Quality.Good)
                    dic[point.Name] = rs.Value;
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
        var address = point.Address;
        var value2 = value.ToString();

        var retry = 3;
        while (true)
        {
            try
            {
                _client.Write(address, value2);
                break;
            }
            catch (Exception ex)
            {
                if (--retry <= 0) throw;
            }
        }

        return true;
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