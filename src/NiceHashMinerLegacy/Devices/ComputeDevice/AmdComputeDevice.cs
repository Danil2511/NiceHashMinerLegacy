﻿using ATI.ADL;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using NiceHashMiner.Devices.Algorithms;
using NiceHashMinerLegacy.Common.Enums;
using NiceHashMinerLegacy.Common.Device;

namespace NiceHashMiner.Devices
{
    public class AmdComputeDevice : ComputeDevice
    {
        private readonly int _adapterIndex; // For ADL
        private readonly int _adapterIndex2; // For ADL2
        private readonly IntPtr _adlContext;
        private bool _powerHasFailed;

        public override int FanSpeed
        {
            get
            {
                var adlf = new ADLFanSpeedValue
                {
                    SpeedType = ADL.ADL_DL_FANCTRL_SPEED_TYPE_RPM
                };
                var result = ADL.ADL_Overdrive5_FanSpeed_Get(_adapterIndex, 0, ref adlf);
                if (result != ADL.ADL_SUCCESS)
                {
                    Helpers.ConsolePrint("ADL", "ADL fan getting failed with error code " + result);
                    return -1;
                }
                return adlf.FanSpeed;
            }
        }

        public override float Temp
        {
            get
            {
                var adlt = new ADLTemperature();
                var result = ADL.ADL_Overdrive5_Temperature_Get(_adapterIndex, 0, ref adlt);
                if (result != ADL.ADL_SUCCESS)
                {
                    Helpers.ConsolePrint("ADL", "ADL temp getting failed with error code " + result);
                    return -1;
                }
                return adlt.Temperature * 0.001f;
            }
        }

        public override float Load
        {
            get
            {
                var adlp = new ADLPMActivity();
                var result = ADL.ADL_Overdrive5_CurrentActivity_Get(_adapterIndex, ref adlp);
                if (result != ADL.ADL_SUCCESS)
                {
                    Helpers.ConsolePrint("ADL", "ADL load getting failed with error code " + result);
                    return -1;
                }
                return adlp.ActivityPercent;
            }
        }

        public override double PowerUsage
        {
            get
            {
                var power = -1;
                if (!_powerHasFailed && _adlContext != IntPtr.Zero && ADL.ADL2_Overdrive6_CurrentPower_Get != null)
                {
                    var result = ADL.ADL2_Overdrive6_CurrentPower_Get(_adlContext, _adapterIndex2, 1, ref power);
                    if (result == ADL.ADL_SUCCESS)
                    {
                        return (double) power / (1 << 8);
                    }

                    // Only alert once
                    Helpers.ConsolePrint("ADL", $"ADL power getting failed with code {result} for GPU {NameCount}. Turning off power for this GPU.");
                    _powerHasFailed = true;
                }

                return power;
            }
        }

        /// <summary>
        /// Get whether AMD device is GCN 4th gen or higher (400/500/Vega)
        /// </summary>
        public bool IsGcn4
        {
            get
            {
                if (Name.Contains("Vega"))
                    return true;
                if (InfSection.ToLower().Contains("polaris"))
                    return true;

                return false;
            }
        }

        public AmdComputeDevice(AmdGpuDevice amdDevice, int gpuCount, bool isDetectionFallback, int adl2Index)
            : base(amdDevice.DeviceID,
                amdDevice.DeviceName,
                true,
                DeviceGroupType.AMD_OpenCL,
                DeviceType.AMD,
                string.Format(Translations.Tr("GPU#{0}"), gpuCount),
                amdDevice.DeviceGlobalMemory)
        {
            Uuid = isDetectionFallback
                ? GetUuid(ID, GroupNames.GetGroupName(DeviceGroupType, ID), Name, DeviceGroupType)
                : amdDevice.Uuid;
            BusID = amdDevice.BusID;
            Codename = amdDevice.Codename;
            InfSection = amdDevice.InfSection;
            AlgorithmSettings = DefaultAlgorithms.GetAlgorithmsForDevice(this);
            DriverDisableAlgos = amdDevice.DriverDisableAlgos;
            Index = ID + AvailableDevices.AvailCpus + AvailableDevices.AvailNVGpus;
            _adapterIndex = amdDevice.Adl1Index;

            ADL.ADL2_Main_Control_Create?.Invoke(ADL.ADL_Main_Memory_Alloc, 0, ref _adlContext);
            _adapterIndex2 = adl2Index;

            // plugin device
            var bd = new BaseDevice(DeviceType.AMD, Uuid, Name, ID);
            PluginDevice = new AMDDevice(bd, amdDevice.BusID, amdDevice.DeviceGlobalMemory, Codename, InfSection);
        }
    }
}
