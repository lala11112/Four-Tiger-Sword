using System;

namespace AssetInventory
{
    /// <summary>Serializable FTP or SFTP endpoint and credential configuration used by remote automation steps.</summary>
    [Serializable]
    public sealed class FTPConnection
    {
        public enum FTPProtocol
        {
            FTP,
            SFTP
        }

        public string key;
        public string name;
        public string host;
        public int port = 21;
        public string username;
        public string encryptedPassword; // Encrypted password
        public FTPProtocol protocol = FTPProtocol.FTP;
        public bool useSsl;
        public bool validateCertificate = true;

        public FTPConnection()
        {
            key = Guid.NewGuid().ToString();
        }

        /// <summary>Creates an independent copy of this FTP Connection; later mutations do not affect the original record.</summary>
        public FTPConnection Clone()
        {
            return new FTPConnection
            {
                key = key,
                name = name,
                host = host,
                port = port,
                username = username,
                encryptedPassword = encryptedPassword,
                protocol = protocol,
                useSsl = useSsl,
                validateCertificate = validateCertificate
            };
        }
        
        /// <summary>Returns port 21 for FTP connections and port 22 for SFTP connections.</summary>
        public int GetDefaultPort()
        {
            return protocol == FTPProtocol.SFTP ? 22 : 21;
        }
    }
}
