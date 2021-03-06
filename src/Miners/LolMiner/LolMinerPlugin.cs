﻿using NHM.MinerPluginToolkitV1;
using NHM.MinerPluginToolkitV1.Configs;
using NHM.MinerPluginToolkitV1.Interfaces;
using NHM.Common.Algorithm;
using NHM.Common.Device;
using NHM.Common.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LolMiner
{
    public partial class LolMinerPlugin : PluginBase, IDevicesCrossReference
    {
        public LolMinerPlugin()
        {
            // mandatory init
            InitInsideConstuctorPluginSupportedAlgorithmsSettings();
            // set default internal settings
            MinerOptionsPackage = PluginInternalSettings.MinerOptionsPackage;
            MinerSystemEnvironmentVariables = PluginInternalSettings.MinerSystemEnvironmentVariables;
            // https://github.com/Lolliedieb/lolMiner-releases/releases | https://bitcointalk.org/index.php?topic=4724735.0 
            MinersBinsUrlsSettings = new MinersBinsUrlsSettings
            {
                BinVersion = "1.06",
                ExePath = new List<string> { "1.06", "lolMiner.exe" },
                Urls = new List<string>
                {
                    "https://github.com/Lolliedieb/lolMiner-releases/releases/download/1.06/lolMiner_v1.06_Win64.zip" // original
                }
            };
            PluginMetaInfo = new PluginMetaInfo
            {
                PluginDescription = "Miner for AMD gpus.",
                SupportedDevicesAlgorithms = SupportedDevicesAlgorithmsDict()
            };
        }

        public override Version Version => new Version(13, 8);

        public override string Name => "lolMiner";

        public override string Author => "info@nicehash.com";

        public override string PluginUUID => "eb75e920-94eb-11ea-a64d-17be303ea466";

        protected readonly Dictionary<string, int> _mappedDeviceIds = new Dictionary<string, int>();

        public override Dictionary<BaseDevice, IReadOnlyList<Algorithm>> GetSupportedAlgorithms(IEnumerable<BaseDevice> devices)
        {
            var supported = new Dictionary<BaseDevice, IReadOnlyList<Algorithm>>();

#warning TEMP disable NVIDIA driver check
            // NVIDIA backend is NOT CUDA but OpenCL!!!!
            //CUDA 9.0+: minimum drivers 384.xx
            //var minDrivers = new Version(384, 0);
            //var isDriverSupported = CUDADevice.INSTALLED_NVIDIA_DRIVERS >= minDrivers;
            var isDriverSupported = true;

            var gpus = devices
                .Where(dev => IsSupportedAMDDevice(dev) || IsSupportedNVIDIADevice(dev, isDriverSupported))
                .Where(dev => dev is IGpuDevice)
                .Cast<IGpuDevice>()
                .OrderBy(gpu => gpu.PCIeBusID);

            var pcieId = 0;
            foreach (var gpu in gpus)
            {
                // map supported NVIDIA devices so indexes match
                _mappedDeviceIds[gpu.UUID] = pcieId;
                ++pcieId;
                var algorithms = GetSupportedAlgorithmsForDevice(gpu as BaseDevice);
                // add only AMD
                if (algorithms.Count > 0 && gpu is AMDDevice) supported.Add(gpu as BaseDevice, algorithms);
            }

            return supported;
        }

        private static bool IsSupportedAMDDevice(BaseDevice dev)
        {
            var isSupported = dev is AMDDevice;
            return isSupported;
        }

        private static bool IsSupportedNVIDIADevice(BaseDevice dev, bool isDriverSupported)
        {
            var isSupported = dev is CUDADevice gpu && gpu.SM_major >= 2 && gpu.IsOpenCLBackendEnabled;
            return isSupported && isDriverSupported;
        }

        protected override MinerBase CreateMinerBase()
        {
            return new LolMiner(PluginUUID, _mappedDeviceIds);
        }

        public async Task DevicesCrossReference(IEnumerable<BaseDevice> devices)
        {
            if (_mappedDeviceIds.Count == 0) return;
            // will block
            var minerBinPath = GetBinAndCwdPaths().Item1;
            var output = await DevicesCrossReferenceHelpers.MinerOutput(minerBinPath, "--benchmark BEAM-III --longstats 60 --devices -1", new List<string> { "Start Benchmark..." });
            var mappedDevs = DevicesListParser.ParseLolMinerOutput(output, devices.ToList());

            foreach (var kvp in mappedDevs)
            {
                var uuid = kvp.Key;
                var indexID = kvp.Value;
                _mappedDeviceIds[uuid] = indexID;
            }
        }

        public override IEnumerable<string> CheckBinaryPackageMissingFiles()
        {
            var pluginRootBinsPath = GetBinAndCwdPaths().Item2;
            return BinaryPackageMissingFilesCheckerHelpers.ReturnMissingFiles(pluginRootBinsPath, new List<string> { "lolMiner.exe" });
        }

        public override bool ShouldReBenchmarkAlgorithmOnDevice(BaseDevice device, Version benchmarkedPluginVersion, params AlgorithmType[] ids)
        {
            if (ids.Count() == 0) return false;
            if (ids.First() == AlgorithmType.BeamV3 && benchmarkedPluginVersion.Major == 13 && benchmarkedPluginVersion.Minor < 7) return true;
            return false;
        }
    }
}
