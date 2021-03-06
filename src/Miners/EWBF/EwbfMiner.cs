﻿using MinerPlugin;
using MinerPluginToolkitV1;
using MinerPluginToolkitV1.Interfaces;
using MinerPluginToolkitV1.ExtraLaunchParameters;
using Newtonsoft.Json;
using NiceHashMinerLegacy.Common.Enums;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static NiceHashMinerLegacy.Common.StratumServiceHelpers;
using System.IO;
using NiceHashMinerLegacy.Common;
using System.Net.Sockets;
using System.Text;

namespace EWBF
{
    public class EwbfMiner : MinerBase
    {
        // default is 2% and can be changed with --fee parameter
        private double DevFee = 0.02;
        private int _apiPort;
        private readonly string _uuid;

        // can mine only one algorithm at a given time
        private AlgorithmType _algorithmType;

        // command line parts
        private string _devices;
        private string _extraLaunchParameters = "";

        public EwbfMiner(string uuid)
        {
            _uuid = uuid;
        }

        private string AlgorithmName(AlgorithmType algorithmType)
        {
            switch (algorithmType)
            {
                case AlgorithmType.ZHash:
                    return "144_5";
                default:
                    return "";
            }
        }

        private string CreateCommandLine(string username)
        {
            // API port function might be blocking
            _apiPort = MinersApiPortsManager.GetAvaliablePortInRange(); // use the default range

            var algo = AlgorithmName(_algorithmType);

            var urlWithPort = GetLocationUrl(_algorithmType, _miningLocation, NhmConectionType.NONE);
            var split = urlWithPort.Split(':');
            var url = split[0];
            var port = split[1];

            var algorithmParam = $"--algo {algo}";
            if (_algorithmType == AlgorithmType.ZHash) algorithmParam += " --pers auto";

            var ret = $"{algorithmParam} --cuda_devices {_devices} --user {username} --server {url} --port {port} --pass x --api 127.0.0.1:{_apiPort} {_extraLaunchParameters}";
            if (!ret.Contains("--fee"))
            {
                ret += " --fee 0";
                DevFee = 0.0d;
            }

            return ret;
        }

        public async override Task<ApiData> GetMinerStatsDataAsync()
        {
            var ad = new ApiData();
            try
            {
                JsonApiResponse resp = null;
                using (var client = new TcpClient("127.0.0.1", _apiPort))
                using (var nwStream = client.GetStream())
                {
                    var bytesToSend = Encoding.ASCII.GetBytes("{\"method\":\"getstat\"}\n");
                    await nwStream.WriteAsync(bytesToSend, 0, bytesToSend.Length);
                    var bytesToRead = new byte[client.ReceiveBufferSize];
                    var bytesRead = await nwStream.ReadAsync(bytesToRead, 0, client.ReceiveBufferSize);
                    var respStr = Encoding.ASCII.GetString(bytesToRead, 0, bytesRead);
                    resp = JsonConvert.DeserializeObject<JsonApiResponse>(respStr);
                    client.Close();
                }

                // return if we got nothing
                var respOK = resp != null && resp.error == null;
                if (respOK == false) return ad;

                var results = resp.result;

                var gpus = _miningPairs.Select(pair => pair.device);
                var perDeviceSpeedInfo = new List<(string uuid, IReadOnlyList<(AlgorithmType, double)>)>();
                var perDevicePowerInfo = new List<(string, int)>();
                var totalSpeed = 0d;
                var totalPowerUsage = 0;
                foreach (var gpu in gpus)
                {
                    var currentDevStats = results.Where(r => r.cudaid == gpu.ID).FirstOrDefault();
                    if (currentDevStats == null) continue;
                    totalSpeed += currentDevStats.speed_sps;
                    perDeviceSpeedInfo.Add((gpu.UUID, new List<(AlgorithmType, double)>() { (_algorithmType, currentDevStats.speed_sps) }));
                    totalPowerUsage += (int)currentDevStats.gpu_power_usage;
                    perDevicePowerInfo.Add((gpu.UUID, (int)currentDevStats.gpu_power_usage));
                }
                ad.AlgorithmSpeedsTotal = new List<(AlgorithmType, double)> { (_algorithmType, totalSpeed) };
                ad.PowerUsageTotal = totalPowerUsage;
            }
            catch (Exception ex)
            {
                //Helpers.ConsolePrint(MinerTag(), ex.Message);
            }

            return ad;
        }

        public async override Task<(double speed, bool ok, string msg)> StartBenchmark(CancellationToken stop, BenchmarkPerformanceType benchmarkType = BenchmarkPerformanceType.Standard)
        {
            // determine benchmark time 
            // settup times
            var benchmarkTime = 90; // in seconds
            switch (benchmarkType)
            {
                case BenchmarkPerformanceType.Quick:
                    benchmarkTime = 60;
                    break;
                case BenchmarkPerformanceType.Standard:
                    benchmarkTime = 90;
                    break;
                case BenchmarkPerformanceType.Precise:
                    benchmarkTime = 120;
                    break;
            }

            // use demo user and disable the watchdog
            var commandLine = CreateCommandLine(MinerToolkit.DemoUser) + " --color 0 --boff";
            var (binPath, binCwd) = GetBinAndCwdPaths();
            var bp = new BenchmarkProcess(binPath, binCwd, commandLine);

            double benchHashesSum = 0;
            double benchHashResult = 0;
            int benchIters = 0;
            int targetBenchIters = Math.Max(1, (int)Math.Floor(benchmarkTime / 30d));

            const string totalSpeed = "Total speed:";
            //const string speedPerGPU = $"GPU{CUDA_D}"// per GPU if we would use multiple GPUs

            bp.CheckData = (string data) => {
                var containsSolRate = data.Contains(totalSpeed) && data.Contains("Sol");
                if (containsSolRate == false) return (benchHashResult, false);
                var (hashrate, found) = MinerToolkit.TryGetHashrateAfter(data, totalSpeed);
                if (!found) return (benchHashResult, false);

                // sum and return
                benchHashesSum += hashrate;
                benchIters++;

                benchHashResult = (benchHashesSum / benchIters) * (1 - DevFee);

                var isFinished = benchIters >= targetBenchIters;
                return (benchHashResult, isFinished);
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
            var binPath = Path.Combine(pluginRootBins, "miner.exe");
            var binCwd = pluginRootBins;
            return (binPath, binCwd);
        }

        protected override void Init()
        {
            bool ok;
            (_algorithmType, ok) = MinerToolkit.GetAlgorithmSingleType(_miningPairs);
            if (!ok) throw new InvalidOperationException("Invalid mining initialization");
            // all good continue on

            // init command line params parts
            var orderedMiningPairs = _miningPairs.ToList();
            orderedMiningPairs.Sort((a, b) => a.device.ID.CompareTo(b.device.ID));
            _devices = string.Join(" ", orderedMiningPairs.Select(p => p.device.ID));
            if (MinerOptionsPackage != null)
            {
                // TODO add ignore temperature checks
                var generalParams = Parser.Parse(orderedMiningPairs, MinerOptionsPackage.GeneralOptions);
                var temperatureParams = Parser.Parse(orderedMiningPairs, MinerOptionsPackage.TemperatureOptions);
                _extraLaunchParameters = $"{generalParams} {temperatureParams}".Trim();
            }
        }

        protected override string MiningCreateCommandLine()
        {
            return CreateCommandLine(_username);
        }
    }
}
