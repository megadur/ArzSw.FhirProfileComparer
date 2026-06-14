# Changelog

All notable changes to the FHIR Profile Comparer will be documented in this file.

## [1.4.1] - 2026-06-10
### Fixed
- **Slice Deduplication:** Duplicate fields caused by FHIR slicing (e.g., `value[x]` vs. specific types) are now cleanly deduplicated based on their FHIR paths. The generic parent entry is hidden if specific slices exist.

## [1.4.0] - 2026-06-09
### Fixed
- **Package Reference:** Corrected the package reference for e-billing data to the official GKV-SV package (`de.gkvsv.erezeptabrechnungsdaten`).
- **Profile Paths:** Added display of `ProfilePaths` in the UI to better trace the origin of elements.

## [1.1.0] - 2026-06-05
### Added
- Initial core implementation of the comparison engine using the official Firely .NET SDK (`Hl7.Fhir.R4`).
- Automatic downloading and extraction of FHIR packages from `simplifier.net`.
- Angular frontend with tabular display of added, removed, and modified elements.
