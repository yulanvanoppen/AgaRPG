using System.Threading.Tasks;
using UnityEngine;

public class TestCSManager : CloudSaveManager
{
    protected override void Awake()
    {
        base.Awake();
        playerId = @"\r\n10Z7WGMIOndHvFYYhLW3idtjOYJ1";
        playerTag = "Anonymous";
        playerSize = 1f;
        playerColor = Color.white;
    }

    public override async Task UpdateSize(float value)
    {
        playerSize = value;
        await Task.Delay(1);
    }

    public override async Task UpdateColor(Color value)
    {
        playerColor = value;
        await Task.Delay(1);
    }
}
