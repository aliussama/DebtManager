namespace DebtManager.Infrastructure.Security;

public interface IKeyStore
{
    byte[] GetOrCreateKey();
}
