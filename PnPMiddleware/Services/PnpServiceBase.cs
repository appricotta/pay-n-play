using System.Security.Cryptography;
using System.Text;
using PnPMiddleware.Configuration;

namespace PnPMiddleware.Services;

public abstract class PnpServiceBase
{
    protected readonly byte[] EncryptionKey;

    protected PnpServiceBase(PaymentApiConfiguration config)
    {
        EncryptionKey = Encoding.UTF8.GetBytes(config.AesEncryptionKey);
    }

    // Abstract properties for provider-specific configuration
    protected abstract string PrivateKeyFileName { get; }
    protected abstract string PublicKeyFileName { get; }
    protected abstract HashAlgorithmName SignHashAlgorithm { get; }
    protected abstract HashAlgorithmName VerifyHashAlgorithm { get; }
    
    protected string GenerateMessageId(string password)
    {
        byte[] rawData = GenerateBinary(password);
        byte[] encryptedData = Encrypt(rawData);
        string messageId = Convert.ToBase64String(encryptedData)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return messageId;
    }

    public string GetPasswordFromMessageId(string messageId)
    {
        // Restore URL-safe Base64 to standard Base64
        string standardBase64 = messageId
            .Replace('-', '+')
            .Replace('_', '/');

        // Add padding if needed
        switch (standardBase64.Length % 4)
        {
            case 2: standardBase64 += "=="; break;
            case 3: standardBase64 += "="; break;
        }

        byte[] decodedData = Convert.FromBase64String(standardBase64);
        byte[] decryptedData = Decrypt(decodedData);
        var (timestamp, randomBytes, password) = DecodeBinaryPassword(decryptedData);
        return password;
    }

    protected byte[] GenerateBinary(string password)
    {
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] timestampBytes = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        byte[] randomBytes = new byte[4];
        RandomNumberGenerator.Fill(randomBytes);

        return timestampBytes.Concat(randomBytes).Concat(passwordBytes).ToArray();
    }

    protected (long timestamp, byte[] randomBytes, string password) DecodeBinaryPassword(byte[] data)
    {
        long timestamp = BitConverter.ToInt64(data, 0);
        byte[] randomBytes = data.Skip(8).Take(4).ToArray();
        string password = Encoding.UTF8.GetString(data, 12, data.Length - 12);
        return (timestamp, randomBytes, password);
    }

    // AES encryption/decryption methods
    protected byte[] Encrypt(byte[] plainBytes)
    {
        using Aes aesAlg = Aes.Create();
        aesAlg.Key = EncryptionKey;
        aesAlg.Mode = CipherMode.ECB;
        aesAlg.Padding = PaddingMode.PKCS7;

        using MemoryStream msEncrypt = new();
        using CryptoStream csEncrypt = new(msEncrypt, aesAlg.CreateEncryptor(), CryptoStreamMode.Write);
        csEncrypt.Write(plainBytes, 0, plainBytes.Length);
        csEncrypt.FlushFinalBlock();

        return msEncrypt.ToArray();
    }

    protected byte[] Decrypt(byte[] cipherBytes)
    {
        using Aes aesAlg = Aes.Create();
        aesAlg.Key = EncryptionKey;
        aesAlg.Mode = CipherMode.ECB;
        aesAlg.Padding = PaddingMode.PKCS7;

        using MemoryStream msDecrypt = new(cipherBytes);
        using CryptoStream csDecrypt = new(msDecrypt, aesAlg.CreateDecryptor(), CryptoStreamMode.Read);
        using MemoryStream resultStream = new();
        csDecrypt.CopyTo(resultStream);

        return resultStream.ToArray();
    }

    // Email hashing
    protected string HashEndUserId(string email)
    {
        using (var sha256 = SHA256.Create())
        {
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(email));
            return Convert.ToHexString(hashBytes).ToLower();
        }
    }

    // RSA signing - can be overridden if needed
    protected virtual string Sign(string plainText)
    {
        var privateKeyText = EmbeddedResource.GetText(PrivateKeyFileName);
        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyText);
        byte[] fileBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] signature = rsa.SignData(fileBytes, SignHashAlgorithm, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }

    // RSA verification - can be overridden if needed
    protected virtual bool Verify(string hash, string plainText)
    {
        var rsa = RSA.Create();
        var publicKeyText = EmbeddedResource.GetText(PublicKeyFileName);
        rsa.ImportFromPem(publicKeyText);
        return rsa.VerifyData(Encoding.UTF8.GetBytes(plainText), Convert.FromBase64String(hash), VerifyHashAlgorithm, RSASignaturePadding.Pkcs1);
    }
}
