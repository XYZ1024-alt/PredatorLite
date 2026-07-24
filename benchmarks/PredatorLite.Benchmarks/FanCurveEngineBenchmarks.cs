using BenchmarkDotNet.Attributes;
using PredatorLite.Core.Models;
using PredatorLite.Core.Services;

namespace PredatorLite.Benchmarks;

[MemoryDiagnoser]
public class FanCurveEngineBenchmarks
{
    private readonly List<FanCurvePoint> _points = FanCurve.CreateDefault().Cpu;

    [Benchmark]
    public int EvaluateSortedCurve() => FanCurveEngine.Evaluate(_points, 72.5, 20);
}
