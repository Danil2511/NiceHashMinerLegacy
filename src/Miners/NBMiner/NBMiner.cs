﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MinerPlugin;
using MinerPluginToolkitV1;
using MinerPluginToolkitV1.Interfaces;
using MinerPluginToolkitV1.ExtraLaunchParameters;
using Newtonsoft.Json;
using NiceHashMinerLegacy.Common;
using NiceHashMinerLegacy.Common.Enums;

namespace NBMiner
{
    public class NBMiner : MinerBase, IDisposable
    {
        private int _apiPort;
        private readonly string _uuid;
        private string _extraLaunchParameters = "";
        private AlgorithmType _algorithmType;
        private readonly Dictionary<int, int> _cudaIDMap;
        private readonly HttpClient _http = new HttpClient();

        private string AlgoName
        {
            get
            {
                switch (_algorithmType)
                {
                    case AlgorithmType.GrinCuckaroo29:
                        return "cuckaroo";
                    case AlgorithmType.GrinCuckatoo31:
                        return "cuckatoo";
                    case AlgorithmType.DaggerHashimoto:
                        return "ethash";
                    default:
                        return "";
                }
            }
        }

        private double DevFee
        {
            get
            {
                switch (_algorithmType)
                {
                    case AlgorithmType.GrinCuckaroo29:
                    case AlgorithmType.GrinCuckatoo31:
                        return 2.0;
                    case AlgorithmType.DaggerHashimoto:
                        return 0.65;
                    default:
                        return 0;
                }
            }
        }

        public NBMiner(string uuid, Dictionary<int, int> cudaIDMap)
        {
            _uuid = uuid;
            _cudaIDMap = cudaIDMap;
        }

        public override async Task<(double speed, bool ok, string msg)> StartBenchmark(CancellationToken stop, BenchmarkPerformanceType benchmarkType = BenchmarkPerformanceType.Standard)
        {
            int benchTime;
            switch (benchmarkType)
            {
                case BenchmarkPerformanceType.Quick:
                    benchTime = 20;
                    break;
                case BenchmarkPerformanceType.Precise:
                    benchTime = 120;
                    break;
                default:
                    benchTime = 60;
                    break;
            }

            var cl = CreateCommandLine(MinerToolkit.DemoUser);
            var (binPath, binCwd) = GetBinAndCwdPaths();
            var bp = new BenchmarkProcess(binPath, binCwd, cl);

            var benchHashes = 0d;
            var benchIters = 0;
            var benchHashResult = 0d;  // Not too sure what this is..
            var targetBenchIters = Math.Max(1, (int)Math.Floor(benchTime / 20d));

            bp.CheckData = (data) =>
            {
                var id = _cudaIDMap.Values.First();
                var (hashrate, found) = data.TryGetHashrateAfter($" - {id}: ");

                if (!found) return (benchHashResult, false);

                benchHashes += hashrate;
                benchIters++;

                benchHashResult = (benchHashes / benchIters) * (1 - DevFee * 0.01);

                return (benchHashResult, benchIters >= targetBenchIters);
            };

            var timeout = TimeSpan.FromSeconds(benchTime + 5);
            var benchWait = TimeSpan.FromMilliseconds(500);
            var t = MinerToolkit.WaitBenchmarkResult(bp, timeout, benchWait, stop);
            return await t;
        }

        protected override (string binPath, string binCwd) GetBinAndCwdPaths()
        {
            var pluginRoot = Path.Combine(Paths.MinerPluginsPath(), _uuid);
            var pluginRootBins = Path.Combine(pluginRoot, "bins");
            var binPath = Path.Combine(pluginRootBins, "nbminer.exe");
            var binCwd = pluginRootBins;
            return (binPath, binCwd);
        }

        protected override string MiningCreateCommandLine()
        {
            return CreateCommandLine(_username);
        }

        private string CreateCommandLine(string username)
        {
            _apiPort = MinersApiPortsManager.GetAvaliablePortInRange();
            var url = StratumServiceHelpers.GetLocationUrl(_algorithmType, _miningLocation, NhmConectionType.STRATUM_TCP);
            
            var devs = string.Join(",", _miningPairs.Select(p => _cudaIDMap[p.device.ID]));
            return $"-a {AlgoName} -o {url} -u {username} --api 127.0.0.1:{_apiPort} -d {devs} -RUN {_extraLaunchParameters}";
        }
        
        public override async Task<ApiData> GetMinerStatsDataAsync()
        {
            var api = new ApiData();
            try
            {
                var result = await _http.GetStringAsync($"http://127.0.0.1:{_apiPort}/api/v1/status");
                var summary = JsonConvert.DeserializeObject<NBMinerJsonResponse>(result);
                api.AlgorithmSpeedsTotal = new[] { (_algorithmType, summary.TotalHashrate ?? 0) };
            }
            catch (Exception e)
            {
            }

            return api;
        }

        protected override void Init()
        {
            bool ok;
            (_algorithmType, ok) = _miningPairs.GetAlgorithmSingleType();
            if (!ok) throw new InvalidOperationException("Invalid mining initialization");

            var orderedMiningPairs = _miningPairs.ToList();
            orderedMiningPairs.Sort((a, b) => a.device.ID.CompareTo(b.device.ID));
            if (MinerOptionsPackage != null)
            {
                // TODO add ignore temperature checks
                var generalParams = Parser.Parse(orderedMiningPairs, MinerOptionsPackage.GeneralOptions);
                var temperatureParams = Parser.Parse(orderedMiningPairs, MinerOptionsPackage.TemperatureOptions);
                _extraLaunchParameters = $"{generalParams} {temperatureParams}".Trim();
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
