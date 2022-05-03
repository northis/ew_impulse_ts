namespace ImpulseFinder.Bot.Dto
{
    /// <summary>
    /// Contains the cTrader API data
    /// </summary>
    internal class ApiCredentials
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApiCredentials"/> class.
        /// </summary>
        /// <param name="clientId">The client identifier.</param>
        /// <param name="secret">The secret.</param>
        public ApiCredentials(string clientId, string secret)
        {
            ClientId = clientId;
            Secret = secret;
        }

        /// <summary>
        /// Gets or sets the client identifier.
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// Gets or sets the auth secret.
        /// </summary>
        public string Secret { get; set; }
    }
}
