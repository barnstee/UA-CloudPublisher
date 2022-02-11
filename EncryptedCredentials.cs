
namespace UA.MQTT.Publisher
{
    using System;
    using System.Net;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// Provides in-memory access to encrypted credentials. It uses the OPC UA app cert to encrypt/decrypt credentials.
    /// </summary>
    public class EncryptedCredentials : NetworkCredential
    {
        public EncryptedCredentials(X509Certificate2 cert, NetworkCredential networkCredential)
        {
            using (RSA rsa = cert.GetRSAPublicKey())
            {
                if ((networkCredential != null) && (networkCredential.UserName != null) && (rsa != null))
                {
                    UserName = Convert.ToBase64String(rsa.Encrypt(Convert.FromBase64String(networkCredential.UserName), RSAEncryptionPadding.Pkcs1));
                }

                if ((networkCredential != null) && (networkCredential.Password != null) && (rsa != null))
                {
                    Password = Convert.ToBase64String(rsa.Encrypt(Convert.FromBase64String(networkCredential.Password), RSAEncryptionPadding.Pkcs1));
                }
            }
        }

        public NetworkCredential Decrypt(X509Certificate2 cert)
        {
            NetworkCredential result = new NetworkCredential();

            using (RSA rsa = cert.GetRSAPrivateKey())
            {
                if ((UserName != null) && (rsa != null))
                {

                    {
                        result.UserName = Convert.ToBase64String(rsa.Decrypt(Convert.FromBase64String(UserName), RSAEncryptionPadding.Pkcs1));
                    }
                }

                if ((Password != null) && (rsa != null))
                {
                    result.Password = Convert.ToBase64String(rsa.Decrypt(Convert.FromBase64String(Password), RSAEncryptionPadding.Pkcs1));
                }
            }

            return result;
        }

        public override bool Equals(object obj)
        {
            if (obj is EncryptedCredentials other)
            {
                return UserName.Equals(other.UserName) && Password.Equals(other.Password);
            }
            else
            {
                return false;
            }
        }

        public override int GetHashCode() => (UserName + Password).GetHashCode();
    }
}
