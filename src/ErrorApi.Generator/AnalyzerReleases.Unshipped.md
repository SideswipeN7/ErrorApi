; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
EAPI001 | ErrorApi | Error    | Duplicate error code
EAPI002 | ErrorApi | Warning  | Route pattern is not a literal
EAPI003 | ErrorApi | Error    | Invalid error catalog member
EAPI004 | ErrorApi | Error    | Invalid HTTP status code
EAPI005 | ErrorApi | Warning  | Unknown error code
EAPI006 | ErrorApi | Info     | Endpoint declares no errors
EAPI007 | ErrorApi | Warning  | Endpoint handler could not be resolved
EAPI008 | ErrorApi | Warning  | Declared error code disagrees with the code in the body
EAPI009 | ErrorApi | Warning  | The walk stopped at a dispatcher
EAPI010 | ErrorApi | Warning  | Declared error is not returned by any endpoint
EAPI011 | ErrorApi | Warning  | Same route mapped more than once without distinct groups
EAPI012 | ErrorApi | Info     | Reachability export stopped at a dispatcher
