﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MinerPlugin;
using MinerPluginToolkitV1.Configs;
using MinerPluginToolkitV1.ExtraLaunchParameters;
using MinerPluginToolkitV1.Interfaces;
using NiceHashMinerLegacy.Common;
using NiceHashMinerLegacy.Common.Algorithm;
using NiceHashMinerLegacy.Common.Device;
using NiceHashMinerLegacy.Common.Enums;

namespace NBMiner
{
    public class NBMinerPlugin : IMinerPlugin, IInitInternals
    {
        public Version Version => new Version(1, 1);
        public string Name => "NBMiner";

        public string Author => "Dillon Newell";

        public string PluginUUID => "d9e7ea80-4bfb-11e9-a481-e144ccd86993";

        private readonly Dictionary<int, int> _mappedCudaIDs = new Dictionary<int, int>();

        private bool isSupportedVersion(int major, int minor)
        {
            Dictionary<int, int> supportedVersions = new Dictionary<int, int>();
            supportedVersions.Add(6, 0);
            supportedVersions.Add(6, 1);
            supportedVersions.Add(7, 0);
            supportedVersions.Add(7, 5);

            if (!supportedVersions.ContainsKey(major)) return false;
            if (!supportedVersions.ContainsValue(minor)) return false;

            foreach(var kvp in supportedVersions)
            {
                if (kvp.Key == major && kvp.Value == minor) return true;
            }
            return false;
        }

        public Dictionary<BaseDevice, IReadOnlyList<Algorithm>> GetSupportedAlgorithms(IEnumerable<BaseDevice> devices)
        {
            var supported = new Dictionary<BaseDevice, IReadOnlyList<Algorithm>>();

            // Require 377.xx
            var minDrivers = new Version(377, 0);
            if (CUDADevice.INSTALLED_NVIDIA_DRIVERS < minDrivers) return supported;

            var cudaGpus = devices
                .Where(dev => dev is CUDADevice gpu && isSupportedVersion(gpu.SM_major, gpu.SM_minor))
                .Cast<CUDADevice>()
                .OrderBy(dev => dev.PCIeBusID);

            var pcieID = 0;
            foreach (var gpu in cudaGpus)
            {
                _mappedCudaIDs[gpu.ID] = pcieID++;
                var algos = GetSupportedAlgorithms(gpu).ToList();
                if (algos.Count > 0) supported.Add(gpu, algos);
            }

            return supported;
        }

        private IEnumerable<Algorithm> GetSupportedAlgorithms(CUDADevice dev)
        {
            const ulong minGrin29Mem = 5UL << 30;
            const ulong minGrin31Mem = 8UL << 30;

            if (dev.GpuRam >= minGrin29Mem)
                yield return new Algorithm(PluginUUID, AlgorithmType.GrinCuckaroo29);

            if (dev.GpuRam >= minGrin31Mem)
                yield return new Algorithm(PluginUUID, AlgorithmType.GrinCuckatoo31);
        }

        public IMiner CreateMiner()
        {
            return new NBMiner(PluginUUID, _mappedCudaIDs)
            {
                MinerOptionsPackage = _minerOptionsPackage
            };
        }

        public bool CanGroup((BaseDevice device, Algorithm algorithm) a, (BaseDevice device, Algorithm algorithm) b)
        {
            return a.algorithm.FirstAlgorithmType == b.algorithm.FirstAlgorithmType;
        }

        #region Internal Settings
        public void InitInternals()
        {
            var pluginRoot = Path.Combine(Paths.MinerPluginsPath(), PluginUUID);
            var fileMinerOptionsPackage = InternalConfigs.InitInternalsHelper(pluginRoot, _minerOptionsPackage);
            if (fileMinerOptionsPackage != null) _minerOptionsPackage = fileMinerOptionsPackage;
        }

        private static MinerOptionsPackage _minerOptionsPackage = new MinerOptionsPackage {};
        #endregion Internal Settings
    }
}
