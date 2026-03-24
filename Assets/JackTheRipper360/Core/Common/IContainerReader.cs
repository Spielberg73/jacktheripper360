using System;
using System.Collections.Generic;
using System.IO;

namespace JackTheRipper360.Core.Common
{
    /// <summary>
    /// Interface for reading container formats (ISO, STFS, GOD, etc.)
    /// </summary>
    public interface IContainerReader : IDisposable
    {
        bool CanRead(Stream stream);
        ContainerInfo ReadHeader(Stream stream);
        IReadOnlyList<ContainerEntry> GetEntries();
        Stream OpenEntry(ContainerEntry entry);
    }
}
