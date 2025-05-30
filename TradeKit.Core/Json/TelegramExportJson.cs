﻿using Newtonsoft.Json;

namespace TradeKit.Core.Json
{
    /// <summary>
    /// "Export from chat" in Telegram entity
    /// </summary>
    public class SymbolDataExportJson
    {
        /// <summary>
        /// Gets or sets the messages array.
        /// </summary>
        [JsonProperty("messages")]
        public TelegramHistoryMessage[] Messages { get; set; }
    }
}
