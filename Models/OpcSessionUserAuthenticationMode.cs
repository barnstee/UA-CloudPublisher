
namespace UA.MQTT.Publisher.Models
{
    /// <summary>
    /// Enum that defines the authentication method to connect to OPC UA
    /// </summary>
    public enum OpcSessionUserAuthenticationMode
    {
        /// <summary>
        /// Anonymous authentication
        /// </summary>
        Anonymous,

        /// <summary>
        /// Username/Password authentication
        /// </summary>
        UsernamePassword
    }
}
