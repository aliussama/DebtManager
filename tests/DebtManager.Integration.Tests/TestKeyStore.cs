using DebtManager.Infrastructure.Security;

namespace DebtManager.Integration.Tests;

public sealed class TestKeyStore : IKeyStore
{
    private readonly byte[] _key = Enumerable.Repeat((byte)0x42, 32).ToArray();
    public byte[] GetOrCreateKey() => _key;
}
