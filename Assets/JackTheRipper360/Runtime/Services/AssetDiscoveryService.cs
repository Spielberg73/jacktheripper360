using System;
using System.Threading;
using System.Threading.Tasks;
using JackTheRipper360.Core.Discovery;

namespace JackTheRipper360.Runtime.Services
{
    /// <summary>
    /// Async wrapper for asset scanning. Runs Core scanners on background threads
    /// and reports progress for Unity UI updates.
    /// Uses thread-safe pending results that the Editor window polls in OnGUI.
    /// </summary>
    public class AssetDiscoveryService
    {
        private readonly AssetDatabase _database;
        private AssetScanner _scanner;
        private CancellationTokenSource _cts;
        private volatile bool _isScanning;

        // Thread-safe pending results for main thread consumption
        private readonly object _resultLock = new object();
        private ScanResult _pendingResult;
        private string _pendingError;
        private ScanProgress _pendingProgress;

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
        /// Must be called from OnGUI or EditorApplication.update to process
        /// pending results from the background scan thread.
        /// </summary>
        public void ProcessPendingCallbacks()
        {
            lock (_resultLock)
            {
                if (_pendingProgress != null)
                {
                    OnProgress?.Invoke(_pendingProgress);
                    _pendingProgress = null;
                }

                if (_pendingResult != null)
                {
                    var result = _pendingResult;
                    _pendingResult = null;
                    OnComplete?.Invoke(result);
                }

                if (_pendingError != null)
                {
                    var error = _pendingError;
                    _pendingError = null;
                    OnError?.Invoke(error);
                }
            }
        }

        /// <summary>
        /// Start scanning a directory asynchronously.
        /// Cancels any previous scan automatically.
        /// </summary>
        public void StartScan(string path)
        {
            // Cancel any previous scan
            if (_isScanning)
                CancelScan();

            _database.Clear();
            lock (_resultLock)
            {
                _pendingResult = null;
                _pendingError = null;
                _pendingProgress = null;
            }
            _cts = new CancellationTokenSource();
            _isScanning = true;

            Task.Run(() =>
            {
                try
                {
                    var result = _scanner.ScanDirectory(path, new Progress<ScanProgress>(p =>
                    {
                        lock (_resultLock) { _pendingProgress = p; }
                    }));
                    _isScanning = false;
                    lock (_resultLock) { _pendingResult = result; }
                }
                catch (Exception ex)
                {
                    _isScanning = false;
                    lock (_resultLock) { _pendingError = $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"; }
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
