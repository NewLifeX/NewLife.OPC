using System.ComponentModel;
using System.Net.NetworkInformation;
using NewLife.IoT.Drivers;
using NewLife.IoT.ThingModels;

namespace NewLife.OPC.Drivers;

/// <summary>
/// 设备网络心跳驱动
/// </summary>
/// <remarks>
/// IoT驱动，通过Ping探测到目标设备的网络情况，并收集延迟数据
/// </remarks>
[Driver("OPC")]
[DisplayName("设备网络心跳")]
public class OPCDriver : DriverBase<Node, OPCParameter>
{
    #region 方法
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

        var p = node.Parameter as OPCParameter;
        foreach (var point in points)
        {
            if (!point.Address.IsNullOrEmpty())
            {
                try
                {
                    var reply = new Ping().Send(point.Address, p.Timeout);
                    if (reply.Status == IPStatus.Success)
                        dic[point.Name] = reply.RoundtripTime;
                    if (p.RetrieveStatus)
                        dic[point.Name + "-Status"] = reply.Status + "";
                }
                catch (Exception ex)
                {
                    dic[point.Name + "-Status"] = ex.GetTrue().Message;
                }
            }
        }

        return dic;
    }
    #endregion
}