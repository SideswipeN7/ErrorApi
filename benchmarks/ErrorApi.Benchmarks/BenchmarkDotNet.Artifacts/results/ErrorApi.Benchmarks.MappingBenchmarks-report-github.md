```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26200.9168)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 16 logical and 8 physical cores
.NET SDK 11.0.100-preview.5.26302.115
  [Host] : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3

Job=ShortRun  Toolchain=InProcessEmitToolchain  IterationCount=3  
LaunchCount=1  WarmupCount=3  

```
| Method                      | Mean      | Error      | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------- |----------:|-----------:|----------:|------:|--------:|-------:|----------:|------------:|
| Raw_TypedResults_Ok         |  4.501 ns |  1.7208 ns | 0.0943 ns |  1.00 |    0.03 | 0.0029 |      24 B |        1.00 |
| Raw_TypedResults_Problem    | 24.349 ns |  4.9607 ns | 0.2719 ns |  5.41 |    0.11 | 0.0201 |     168 B |        7.00 |
| Lookup_FindError            |  5.231 ns |  0.2687 ns | 0.0147 ns |  1.16 |    0.02 |      - |         - |        0.00 |
| Lookup_FindError_miss       |  5.804 ns |  0.2549 ns | 0.0140 ns |  1.29 |    0.02 |      - |         - |        0.00 |
| Lookup_FindErrorForInstance |  2.514 ns |  0.1267 ns | 0.0069 ns |  0.56 |    0.01 |      - |         - |        0.00 |
| Lookup_TryGetEndpointErrors |  3.396 ns |  0.2163 ns | 0.0119 ns |  0.75 |    0.01 |      - |         - |        0.00 |
| Core_Success                |  4.402 ns |  1.1492 ns | 0.0630 ns |  0.98 |    0.02 | 0.0029 |      24 B |        1.00 |
| Core_Failure                | 60.113 ns | 18.1459 ns | 0.9946 ns | 13.36 |    0.31 | 0.0362 |     304 B |       12.67 |
| ErrorOr_Success             |  4.550 ns |  1.1178 ns | 0.0613 ns |  1.01 |    0.02 | 0.0029 |      24 B |        1.00 |
| ErrorOr_Failure             | 70.203 ns | 10.5797 ns | 0.5799 ns | 15.60 |    0.30 | 0.0362 |     304 B |       12.67 |
| OneOf_Success               |  5.027 ns |  1.6278 ns | 0.0892 ns |  1.12 |    0.03 | 0.0029 |      24 B |        1.00 |
| OneOf_Failure               | 66.640 ns | 15.9067 ns | 0.8719 ns | 14.81 |    0.32 | 0.0362 |     304 B |       12.67 |
| LanguageExt_Success         |  5.903 ns |  0.9953 ns | 0.0546 ns |  1.31 |    0.03 | 0.0029 |      24 B |        1.00 |
| LanguageExt_Failure         | 76.062 ns | 30.8601 ns | 1.6915 ns | 16.91 |    0.45 | 0.0362 |     304 B |       12.67 |
| FluentResults_Success       |  5.487 ns |  1.2518 ns | 0.0686 ns |  1.22 |    0.03 | 0.0029 |      24 B |        1.00 |
| FluentResults_Failure       | 80.490 ns | 35.1649 ns | 1.9275 ns | 17.89 |    0.49 | 0.0391 |     328 B |       13.67 |
| Ardalis_Success             |  4.157 ns |  0.2016 ns | 0.0110 ns |  0.92 |    0.02 | 0.0029 |      24 B |        1.00 |
| Ardalis_Failure             | 73.884 ns |  8.1253 ns | 0.4454 ns | 16.42 |    0.31 | 0.0362 |     304 B |       12.67 |
| Cfe_Success                 |  4.985 ns |  4.0037 ns | 0.2195 ns |  1.11 |    0.05 | 0.0029 |      24 B |        1.00 |
| Cfe_Failure                 | 64.181 ns | 14.8650 ns | 0.8148 ns | 14.26 |    0.30 | 0.0362 |     304 B |       12.67 |
