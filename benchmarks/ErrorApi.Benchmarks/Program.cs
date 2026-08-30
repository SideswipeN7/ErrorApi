using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

// In-process on purpose: BenchmarkDotNet's default toolchain compiles and launches a fresh executable
// per benchmark, which per-hash application control policies (Smart App Control) love to block. The
// numbers here are nanosecond-scale mapping calls, which the in-process emit toolchain measures fine.
var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance))
    .WithOptions(ConfigOptions.DisableOptimizationsValidator);

BenchmarkRunner.Run<ErrorApi.Benchmarks.MappingBenchmarks>(config, args);
