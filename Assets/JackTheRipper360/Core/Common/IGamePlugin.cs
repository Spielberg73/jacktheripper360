using System.Collections.Generic;

namespace JackTheRipper360.Core.Common
{
    /// <summary>
    /// Interface for game-specific plugins that provide custom parsers
    /// for proprietary archive/asset formats used by individual Xbox 360 games.
    /// </summary>
    public interface IGamePlugin
    {
        /// <summary>Unique identifier for this plugin (e.g., "halo3", "gears_of_war").</summary>
        string Id { get; }

        /// <summary>Display name of the game.</summary>
        string GameName { get; }

        /// <summary>List of Title IDs this plugin supports (e.g., "4D5307E6" for Halo 3).</summary>
        IReadOnlyList<string> SupportedTitleIds { get; }

        /// <summary>File extensions this plugin can handle.</summary>
        IReadOnlyList<string> SupportedExtensions { get; }

        /// <summary>Custom asset parsers provided by this plugin.</summary>
        IReadOnlyList<IAssetParser> GetParsers();

        /// <summary>Custom container readers provided by this plugin.</summary>
        IReadOnlyList<IContainerReader> GetContainerReaders();

        /// <summary>Check if this plugin can handle a given file.</summary>
        bool CanHandle(string filePath, byte[] headerBytes);
    }

    /// <summary>
    /// Registry that manages game-specific plugins.
    /// </summary>
    public class PluginRegistry
    {
        private readonly List<IGamePlugin> _plugins = new List<IGamePlugin>();
        private readonly Dictionary<string, IGamePlugin> _byId = new Dictionary<string, IGamePlugin>();
        private readonly Dictionary<string, IGamePlugin> _byTitleId = new Dictionary<string, IGamePlugin>();

        public IReadOnlyList<IGamePlugin> AllPlugins => _plugins.AsReadOnly();

        public void Register(IGamePlugin plugin)
        {
            if (_byId.ContainsKey(plugin.Id)) return;

            _plugins.Add(plugin);
            _byId[plugin.Id] = plugin;

            foreach (string titleId in plugin.SupportedTitleIds)
            {
                _byTitleId[titleId.ToUpperInvariant()] = plugin;
            }
        }

        public void Unregister(string pluginId)
        {
            if (!_byId.TryGetValue(pluginId, out var plugin)) return;

            _plugins.Remove(plugin);
            _byId.Remove(pluginId);

            foreach (string titleId in plugin.SupportedTitleIds)
                _byTitleId.Remove(titleId.ToUpperInvariant());
        }

        public IGamePlugin GetByTitleId(string titleId)
        {
            _byTitleId.TryGetValue(titleId?.ToUpperInvariant() ?? "", out var plugin);
            return plugin;
        }

        public IGamePlugin GetById(string id)
        {
            _byId.TryGetValue(id, out var plugin);
            return plugin;
        }

        /// <summary>
        /// Find a plugin that can handle the given file.
        /// </summary>
        public IGamePlugin FindPlugin(string filePath, byte[] headerBytes)
        {
            foreach (var plugin in _plugins)
            {
                if (plugin.CanHandle(filePath, headerBytes))
                    return plugin;
            }
            return null;
        }

        /// <summary>
        /// Get all parsers from all registered plugins.
        /// </summary>
        public List<IAssetParser> GetAllParsers()
        {
            var parsers = new List<IAssetParser>();
            foreach (var plugin in _plugins)
                parsers.AddRange(plugin.GetParsers());
            return parsers;
        }

        /// <summary>
        /// Get all container readers from all registered plugins.
        /// </summary>
        public List<IContainerReader> GetAllContainerReaders()
        {
            var readers = new List<IContainerReader>();
            foreach (var plugin in _plugins)
                readers.AddRange(plugin.GetContainerReaders());
            return readers;
        }
    }
}
