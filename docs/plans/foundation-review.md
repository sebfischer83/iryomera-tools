# Foundation review remediation

Scope: Phase 1 only. The previously prepared, unpublished Phase 2 work is preserved locally on `archive/phase-2-local` (commit fb08269); it is not part of this branch, solution, CI or package changes.

1. Add independent, lossless medical annotation contracts for HPO and MAxO. Preserve exact predicates, qualifiers, negation state, source-frequency strings, multi-way context, references and raw source fields. Keep existing binary relation kinds and existing valid JSON compatible. Record the distinction and projection restrictions in an ADR; do not implement import adapters.
2. Mark every schema-required schemaVersion as JSON-required while keeping defaults for C# construction. Add an offline shared entry point combining a mature JSON Schema evaluator with existing semantic validators. Keep optional/additive fields and extension round trips. Test raw and nested omissions, malformed input, duplicates, explicit nulls and semantic errors.
3. Multi-target SDK/Localization for net8.0 and net10.0. Keep the toolchain on the .NET 10 SDK. Use supported System.Text.Json packages for identical wire behavior. Test both targets and run a small net8.0 Godot project against packaged libraries, including serialization and German/English rendering.
4. Replace obsolete CI action versions with verified official releases. Run build/tests/example/pack and Godot smoke test on Windows and Linux. Link the previous successful run and the new exact revision/run in verification docs; do not describe an unobserved run as successful.

No real medical datasets, clinical review, source import/approval pipeline, UI/database or distribution service is in this task.
