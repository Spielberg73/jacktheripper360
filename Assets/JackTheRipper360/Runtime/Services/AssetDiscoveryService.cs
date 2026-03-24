using System;
using System.Threading;
using System.Threading.Tasks;
using JackTheRipper360.Core.Discovery;

namespace JackTheRipper360.Runtime.Services
{
    /// <summary>
    /// Async wrapper for asset scanning. Runs Core scanners on background threads
    /// and reports progress for Unity UI updates.
    /// </summary>
    public class AssetDiscoveryService
    {
        private readonly AssetDatabase _database;
        private AssetScanner _scanner;
        private CancellationTokenSource _cts;
        private bool _isScanning;

        public AssetDatabase Database => _database;
        public bool IsScanning => _isScanning;

        public event Action<ScanProgress> OnProgress;
        public event Action<ScanResult> OnComplete;
        public event Action<string> OnError;

        public AssetDiscoveryService()
        {
            _database = new AssetDatabase();
            _scanner = new AssetScanner(_database);
        }

        /// <summary>
        /// Start scanning a directory asynchronously.
        /// </summary>
        public void StartScan(string path)
        {
            if (_isScanning)
            {
                OnError?.Invoke("A scan is already in progress.");
                return;
            }

            _database.Clear();
            _cts = new CancellationTokenSource();
            _isScanning = true;

            var progress = new Progress<ScanProgress>(p => OnProgress?.Invoke(p));

            Task.Run(() =>
            {
                try
                {
                    var result = _scanner.ScanDirectory(path, progress);
                    _isScanning = false;
                    OnComplete?.Invoke(result);
                }
                catch (Exception ex)
                {
                    _isScanning = false;
                    OnError?.Invoke(ex.Message);
                }
            }, _cts.Token);
        }

        /// <summary>
        /// Cancel the current scan.
        /// </summary>
        public void CancelScan()
        {
            _cts?.Cancel();
            _isScanning = false;
        }

        /// <summary>
        /// Scan a single file synchronously.
        /// </summary>
        public ScanResult ScanFile(string filePath)
        {
            var result = new ScanResult();
            _scanner.ScanFile(filePath, result);
            return result;
        }
    }
}
