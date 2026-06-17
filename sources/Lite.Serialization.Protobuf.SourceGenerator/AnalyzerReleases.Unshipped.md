; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
LITEGRPC001 | LiteGrpc | Error | Unsupported member type
LITEGRPC002 | LiteGrpc | Error | Type cannot be constructed for deserialization
LITEGRPC003 | LiteGrpc | Error | Duplicate proto tag
LITEGRPC004 | LiteGrpc | Warning | Generic types not supported as a serialization root
LITEGRPC005 | LiteGrpc | Warning | No serializable members
LITEGRPC100 | LiteGrpc | Info | gRPC marshaller interception skipped
LITEGRPC900 | LiteGrpc | Error | Generator crashed
