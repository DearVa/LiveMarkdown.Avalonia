# Night train · 夜班车

The last train out of Shanghai runs beside the river for a few minutes before the lights of the city fall away. 在车窗的倒影里，站台的灯慢慢远去，the river turns silver, and someone finally looks up from their book.

The board changes from *boarding* to **ready to depart**. Car seven has a reading lamp at every seat; tap `light.on()` for a little company, or check the [route map](https://example.com/route) for the next stop.

```csharp
var nextStop = "苏州 · Suzhou";
await display.WriteAsync(nextStop);
```
