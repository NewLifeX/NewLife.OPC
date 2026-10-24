using NewLife.IoT.Drivers;
using NewLife.IoT.ThingModels;
using NewLife.Log;
using NewLife.OPC.Drivers;

XTrace.UseConsole();

var driver = new OpcUaDriver();
var pm = new OpcUaParameter();
var node = await driver.OpenAsync(null, pm);

var point = new PointModel
{
    Name = "newlife",
    Address = "newlifex.com",
    Type = "Int32",
};
var point2 = new PointModel
{
    Name = "google",
    Address = "google.com",
    Type = "Int32",
};

var result = await driver.ReadAsync(node, [point, point2]);
if (result.IsSuccess)
{
    for (var i = 0; i < result.Points.Length; i++)
    {
        XTrace.WriteLine("{0}\t= {1}", result.Points[i].Name, result.Values[i]);
    }
}
else
{
    XTrace.WriteLine("读取失败：{0} {1}", result.Code, result.Message);
}

await driver.CloseAsync(node);
