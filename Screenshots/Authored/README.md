# Authored visual evidence

`Hero0.png` through `Hero2.png` show the reference effects. The named technique images compare the same reference composition at a fixed simulation clock. `Proof/Hero3.png` shows Void Conduit; `Proof/Hero4.png` shows Opal Transfer, composed from an existing mote module and a new module. The named technique images inside `Proof/` select Opal Transfer.

`capture-metadata.json` records the standalone capture build and procedure. These images demonstrate appearance, not pixel identity or performance. Connected ribbon joins, particle orientation and gradient interpolation differ between adapters. Later LineRenderer HDR reconstruction and benchmark-only fixes are documented in the measured findings and source annotations.

`FinalPreview.png` records Void Conduit in the reopened Editor after the final acceptance pass. It is a camera capture of the live artist-library preview, rather than a timed standalone frame.

`Spread5000-Corrected.png` is an Editor sanity view of the corrected wide camera at 5,000 effects, with all 10,000 endpoints inside its frustum. The exact endpoint/particle check is recorded in `Documentation/Validation/spread-frustum.json`. It is not one of the timed standalone samples.

`Diagnostic-ClippedSpread5000.png` preserves the earlier camera failure. Its far clip excluded much of the field. It must not be used as evidence of successful full-field rendering; corresponding old Spread timing rows are excluded from the consolidated comparison.
