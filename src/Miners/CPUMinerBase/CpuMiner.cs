﻿using MinerPlugin;
using MinerPluginToolkitV1;
using MinerPluginToolkitV1.Interfaces;
using MinerPluginToolkitV1.ExtraLaunchParameters;
using NiceHashMinerLegacy.Common.Enums;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using static NiceHashMinerLegacy.Common.StratumServiceHelpers;
using NiceHashMinerLegacy.Common.Device;
using System.Collections.Generic;
using System.Globalization;
using NiceHashMinerLegacy.Common;
using System.IO;

namespace CPUMinerBase
{
    public class CpuMiner : MinerBase, IAfterStartMining
    {
        private readonly string _uuid;

        // cpuminer can mine only one algorithm at a given time
        private AlgorithmType _algorithmType;

        // command line parts
        private ulong _affinityMask = 0;
        private string _extraLaunchParameters = "";
        private int _apiPort;

        private ApiDataHelper apiReader = new ApiDataHelper(); // consider replacing with HttpClient

        public CpuMiner(string uuid)
        {
            _uuid = uuid;
        }

        protected virtual string AlgorithmName(AlgorithmType algorithmType)
        {
            switch (algorithmType)
            {
                case AlgorithmType.Lyra2Z: return "lyra2z";
            }
            // TODO throw exception
            return "";
        }

        public async override Task<ApiData> GetMinerStatsDataAsync()
        {
            var summaryApiResult = await apiReader.GetApiDataAsync(_apiPort, ApiDataHelper.GetHttpRequestNhmAgentStrin("summary"));
            double totalSpeed = 0;
            int totalPower = 0;
            if (!string.IsNullOrEmpty(summaryApiResult))
            {
                // TODO return empty
                try
                {
                    var summaryOptvals = summaryApiResult.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var optvalPairs in summaryOptvals)
                    {
                        var pair = optvalPairs.Split(new char[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                        if (pair.Length != 2) continue;
                        if (pair[0] == "KHS")
                        {
                            totalSpeed = double.Parse(pair[1], CultureInfo.InvariantCulture) * 1000; // HPS
                        }
                    }
                }
                catch
                { }
            }
            var ad = new ApiData();
            var total = new List<(AlgorithmType, double)>();
            total.Add((_algorithmType, totalSpeed));
            ad.AlgorithmSpeedsTotal = total;
            ad.PowerUsageTotal = totalPower;
            // cpuMiner is single device so no need for API

            return ad;
        }

        public async override Task<(double speed, bool ok, string msg)> StartBenchmark(CancellationToken stop, BenchmarkPerformanceType benchmarkType = BenchmarkPerformanceType.Standard)
        {
            // determine benchmark time 
            // settup times
            var benchmarkTime = 20; // in seconds
            switch (benchmarkType)
            {
                case BenchmarkPerformanceType.Quick:
                    benchmarkTime = 20;
                    break;
                case BenchmarkPerformanceType.Standard:
                    benchmarkTime = 60;
                    break;
                case BenchmarkPerformanceType.Precise:
                    benchmarkTime = 120;
                    break;
            }

            var algo = AlgorithmName(_algorithmType);

            var commandLine = $"--algo={algo} --benchmark --time-limit {benchmarkTime} {_extraLaunchParameters}";

            var (binPath, binCwd) = GetBinAndCwdPaths();
            var bp = new BenchmarkProcess(binPath, binCwd, commandLine);
            // TODO benchmark process add after benchmark

            // make sure this is culture invariant
            // TODO implement fallback average, final benchmark 
            bp.CheckData = (string data) => {
                if (double.TryParse(data, out var parsedSpeed)) return (parsedSpeed, true);
                return (0d, false);
            };

            var benchmarkTimeout = TimeSpan.FromSeconds(benchmarkTime + 5);
            var benchmarkWait = TimeSpan.FromMilliseconds(500);
            var t = MinerToolkit.WaitBenchmarkResult(bp, benchmarkTimeout, benchmarkWait, stop);
            return await t;
        }

        protected override (string, string) GetBinAndCwdPaths()
        {
            var pluginRoot = Path.Combine(Paths.MinerPluginsPath(), _uuid);
            var pluginRootBins = Path.Combine(pluginRoot, "bins");
            var binPath = Path.Combine(pluginRootBins, "cpuminer.exe");
            var binCwd = pluginRootBins;
            return (binPath, binCwd);
        }

        protected override string MiningCreateCommandLine()
        {
            // API port function might be blocking
            _apiPort = MinersApiPortsManager.GetAvaliablePortInRange(); // use the default range
            // instant non blocking
            var url = GetLocationUrl(_algorithmType, _miningLocation, NhmConectionType.STRATUM_TCP);
            var algo = AlgorithmName(_algorithmType);

            var commandLine = $"--algo={algo} --url={url} --user={_username} --api-bind={_apiPort} {_extraLaunchParameters}";
            return commandLine;
        }

        protected override void Init()
        {
            bool ok;
            (_algorithmType, ok) = MinerToolkit.GetAlgorithmSingleType(_miningPairs);
            if (!ok) throw new InvalidOperationException("Invalid mining initialization");

            var cpuDevice = _miningPairs.Select(kvp => kvp.device).FirstOrDefault();
            if (cpuDevice is CPUDevice cpu)
            {
                // TODO affinity mask stuff
                //_affinityMask
            }
            var miningPairsList = _miningPairs.ToList();
            if (MinerOptionsPackage != null)
            {
                var generalParams = Parser.Parse(miningPairsList, MinerOptionsPackage.GeneralOptions);
                var temperatureParams = Parser.Parse(miningPairsList, MinerOptionsPackage.TemperatureOptions);
                _extraLaunchParameters = $"{generalParams} {temperatureParams}".Trim();
            }
        }

        public void AfterStartMining()
        {
            int pid = -1;
            if (_miningProcess != null && _miningProcess.Handle != null) {
                pid = _miningProcess.Handle.Id;
            }
            // TODO C# can have this shorter
            if (_affinityMask != 0 && pid != -1)
            {
                var (ok, msg) = ProcessHelpers.AdjustAffinity(pid, _affinityMask);
                // TODO log what is going on is it ok or not 
            }
        }
    }
}
