# IRS AIR schemas

The IRS XML schemas the IRS files are built and checked against (`IrsXmlService`,
`IrsAirXml`). Every generated form file and manifest is validated against the schemas of
its forms' tax year before it is saved, so a file the IRS would reject on format never
reaches the IRS.

Each tax year has its own schemas and its own namespace
(`urn:us:gov:treasury:irs:ext:aca:air:ty25` for 2025, `ty24` for 2024, ...). The app picks the
folder `TY{year}` for the tax year being filed and reads the namespace from its `EXT` schema;
a tax year without a folder is refused with a message saying so.

| Folder | Source (irs.gov/pub/irs-schema) |
|---|---|
| `TY2025` | "Tax year 2025 ... AIR release memo, XML schemas and business rules version 1.0": `fs26-aca-air-iep-b-and-c-upstream-ty2025-v1-0-XML-library-8-7-for-ats-and-production-ui.zip` (24 Oct 2025) |
| `TY2024` | "TY2024 ACA AIR schemas v1.0": `TY24-schemas-1094-b-1094-c-1095-b-1095-c-v1.zip` (3 Dec 2024) |
| `TY2023` | "TY2023 ACA Air Schemas v1.0": `aca-schemas-ty2023-v1.0.zip` (9 Aug 2023) |

Only the files the UI channel needs are kept: `COMMON`, `EXT`, and in `MSG` the 1094/1095-B
and -C upstream messages, the business header, the user-interface header and the WS timestamp.

## Adding a tax year

1. Download the year's schema package from the IRS AIR page ("Tax year YYYY ... XML schemas and
   business rules"). If it is not on irs.gov, the IRS sends it to registered software developers
   through e-Services.
2. Make a folder `TYYYYY` here with `COMMON`, `EXT` and the `MSG` files listed above.
3. Run the IRS tests (`IrsAirXmlTests`): every year in this folder is built and validated. If the
   IRS changed the form structure, the failures say where; adjust `IrsAirXml`.
