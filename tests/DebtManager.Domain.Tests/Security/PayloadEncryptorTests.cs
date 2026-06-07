using DebtManager.Infrastructure.Security;
using Xunit;

namespace DebtManager.Domain.Tests.Security;

public class PayloadEncryptorTests
{
    private readonly PayloadEncryptor _encryptor;

    public PayloadEncryptorTests()
    {
        var keyStore = new TestKeyStore();
        _encryptor = new PayloadEncryptor(keyStore);
    }

    [Fact]
    public void Encrypt_ThenDecrypt_ReturnsOriginal()
    {
        // Arrange
        var original = "This is sensitive financial data: 100,000 EGP";

        // Act
        var encrypted = _encryptor.Encrypt(original);
        var decrypted = _encryptor.Decrypt(encrypted);

        // Assert
        Assert.Equal(original, decrypted);
        Assert.NotEqual(original, encrypted);
    }

    [Fact]
    public void Encrypt_SameInput_ProducesDifferentOutput()
    {
        // Arrange
        var input = "Same input";

        // Act
        var encrypted1 = _encryptor.Encrypt(input);
        var encrypted2 = _encryptor.Encrypt(input);

        // Assert - should be different due to random nonce
        Assert.NotEqual(encrypted1, encrypted2);

        // But both should decrypt to same value
        Assert.Equal(input, _encryptor.Decrypt(encrypted1));
        Assert.Equal(input, _encryptor.Decrypt(encrypted2));
    }

    [Fact]
    public void Encrypt_EmptyString_ReturnsEmpty()
    {
        // Act & Assert
        Assert.Equal("", _encryptor.Encrypt(""));
        Assert.Equal("", _encryptor.Decrypt(""));
    }

    [Fact]
    public void Encrypt_Null_ReturnsNull()
    {
        // Act & Assert
        Assert.Null(_encryptor.Encrypt(null!));
        Assert.Null(_encryptor.Decrypt(null!));
    }

    [Fact]
    public void Encrypt_LargePayload_WorksCorrectly()
    {
        // Arrange
        var largePayload = new string('X', 100_000); // 100KB

        // Act
        var encrypted = _encryptor.Encrypt(largePayload);
        var decrypted = _encryptor.Decrypt(encrypted);

        // Assert
        Assert.Equal(largePayload, decrypted);
    }

    [Fact]
    public void Encrypt_JsonPayload_PreservesContent()
    {
        // Arrange
        var json = """
        {
            "obligationId": "12345678-1234-1234-1234-123456789012",
            "amount": 50000.00,
            "currency": "EGP",
            "notes": "????? ????? ?????"
        }
        """;

        // Act
        var encrypted = _encryptor.Encrypt(json);
        var decrypted = _encryptor.Decrypt(encrypted);

        // Assert
        Assert.Equal(json, decrypted);
    }

    [Fact]
    public void Decrypt_TamperedData_ThrowsCryptographicException()
    {
        // Arrange
        var original = "Sensitive data";
        var encrypted = _encryptor.Encrypt(original);
        var bytes = Convert.FromBase64String(encrypted);

        // Tamper with the ciphertext
        bytes[20] ^= 0xFF;
        var tampered = Convert.ToBase64String(bytes);

        // Act & Assert
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
            _encryptor.Decrypt(tampered));
    }

    [Fact]
    public void SecureEquals_SameStrings_ReturnsTrue()
    {
        // Act & Assert
        Assert.True(PayloadEncryptor.SecureEquals("password123", "password123"));
        Assert.True(PayloadEncryptor.SecureEquals("", ""));
    }

    [Fact]
    public void SecureEquals_DifferentStrings_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(PayloadEncryptor.SecureEquals("password123", "password124"));
        Assert.False(PayloadEncryptor.SecureEquals("abc", "abcd"));
    }

    [Fact]
    public void SecureEquals_NullHandling_WorksCorrectly()
    {
        // Act & Assert
        Assert.True(PayloadEncryptor.SecureEquals(null, null));
        Assert.False(PayloadEncryptor.SecureEquals("abc", null));
        Assert.False(PayloadEncryptor.SecureEquals(null, "abc"));
    }

    [Fact]
    public void HashForComparison_SameInputSameSalt_ProducesSameHash()
    {
        // Arrange
        var value = "my-api-key";
        var salt = "fixed-salt";

        // Act
        var hash1 = PayloadEncryptor.HashForComparison(value, salt);
        var hash2 = PayloadEncryptor.HashForComparison(value, salt);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashForComparison_DifferentSalt_ProducesDifferentHash()
    {
        // Arrange
        var value = "my-api-key";

        // Act
        var hash1 = PayloadEncryptor.HashForComparison(value, "salt1");
        var hash2 = PayloadEncryptor.HashForComparison(value, "salt2");

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    private sealed class TestKeyStore : IKeyStore
    {
        private readonly byte[] _key = new byte[32]; // Fixed key for testing

        public TestKeyStore()
        {
            // Use a deterministic key for testing
            for (int i = 0; i < 32; i++)
                _key[i] = (byte)i;
        }

        public byte[] GetOrCreateKey() => _key;
    }
}
