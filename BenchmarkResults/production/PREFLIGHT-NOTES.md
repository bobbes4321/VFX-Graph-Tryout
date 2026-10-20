# Preflight history

- preflight-functional: initial twelve-cell functional smoke run. Performance was plausible and visual/particle checks passed, but instancing flags were not yet exported explicitly. It is excluded from final findings.
- preflight-metadata: interrupted partial full run. The harness inherited default false instancing flags and a zero for an unmeasured SetPass counter. No completion marker; excluded from final findings.
- smoke: final twelve-cell preflight, with explicit instancing configuration and optional-counter handling.
- reference: final full matrix. Only this completed, audited release run drives the complete-ability report.

The first standalone visual capture preceded the final metadata-only changes. Visuals are recaptured with the final release player after the benchmark.
