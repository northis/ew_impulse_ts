using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace TradeKit.Config
{
    /// <summary>
    /// Keeps the signal state between the sessions
    /// </summary>
    public class StateKeeper
    {
        private readonly string m_FilePath;
        private readonly object m_SyncRoot = new();

        public StateKeeper(string filePath)
        {
            m_FilePath = filePath;
        }

        public StateKeeper() : this(
            Path.Combine(Environment.CurrentDirectory, "config.json"))
        {
        }

        /// <summary>
        /// Gets or sets the states.
        /// </summary>
        public MainState MainState { get; set; }

        /// <summary>
        /// Loads the sates.
        /// </summary>
        /// <exception cref="Exception">Cannot parse the file {m_FilePath}</exception>
        private void Load()
        {
            lock (m_SyncRoot)
            {
                MainState = JsonConvert.DeserializeObject<MainState>(File.ReadAllText(m_FilePath));
            }

            if (MainState == null)
            {
                throw new Exception($"Cannot parse the file {m_FilePath}");
            }
        }

        /// <summary>
        /// Resets the state.
        /// </summary>
        public void ResetState()
        {
            lock (m_SyncRoot)
            {
                if (File.Exists(m_FilePath))
                {
                    File.Delete(m_FilePath);
                }
            }
        }
        
        /// <summary>
        /// Initializes with the specified symbols.
        /// </summary>
        /// <param name="symbols">The symbols.</param>
        public void Init(string[] symbols)
        {
            lock (m_SyncRoot)
            {
                symbols ??= Array.Empty<string>();
                if (File.Exists(m_FilePath))
                {
                    Load();
                }

                Dictionary<string, SymbolState> prevDic = MainState?.States;
                MainState = new MainState
                {
                    States = new Dictionary<string, SymbolState>()
                };
                foreach (string symbol in symbols)
                {
                    if (prevDic == null || !prevDic.TryGetValue(symbol, out SymbolState prevState))
                    {
                        prevState = new SymbolState();
                    }

                    MainState.States.Add(symbol, prevState);
                }
            }
        }

        /// <summary>
        /// Saves the current state.
        /// </summary>
        public void Save()
        {
            lock (m_SyncRoot)
            {
                string json = JsonConvert.SerializeObject(MainState, Formatting.Indented);
                File.WriteAllText(m_FilePath, json);
            }
        }
    }
}
