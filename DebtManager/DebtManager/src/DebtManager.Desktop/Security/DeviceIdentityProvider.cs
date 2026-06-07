using System.IO;
using System.Text.Json;

namespace DebtManager.Desktop.Security;

public sealed class DeviceIdentityProvider
{
    private readonly string _path;

    public DeviceIdentityProvider()
    {
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DebtManager",
            "device.json");
    }

    public Guid GetOrCreateDeviceId()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        if (File.Exists(_path))
        {
            var json = File.ReadAllText(_path);
            var dto = JsonSerializer.Deserialize<DeviceDto>(json);
            if (dto is not null && dto.DeviceId != Guid.Empty)
                return dto.DeviceId;
        }

        var newId = Guid.NewGuid();
        var outJson = JsonSerializer.Serialize(new DeviceDto(newId), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, outJson);
        return newId;
    }

    private sealed record DeviceDto(Guid DeviceId);
}
