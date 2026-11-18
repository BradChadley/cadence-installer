# Code signing: what it would take

Nothing here is set up. This is the plan, for Bradley to decide on.

## Today, unsigned

- **This installer:** Windows shows "Windows protected your PC" the first time. The person clicks "More info", then "Run anyway".
- **Cadence, which it installs:** no second warning. The installer downloads it itself, so Windows does not mark it as "from the internet". It is checked before it runs (see the README).
- **Cadence's own download from GitHub:** the same warning as this installer.

So this installer already cuts the warnings a person meets from two to one. Signing is what removes the last one.

## The service: Azure Artifact Signing

Microsoft's own signing service, the one chosen "for later". Microsoft holds the certificate, so there is no key file or USB token to keep safe. Signing happens from his PC or from GitHub through his Azure sign-in.

1. **An Azure account** with a payment method. The Basic plan was about $10 a month; check the price on Microsoft's page when signing up.
2. **An Artifact Signing account** in the Azure portal, then an **identity check** of Bradley as a person. Microsoft asks for a government ID and checks it. It takes from a day to a few days, and **the name on the certificate is his legal name**, which everyone who looks at the file's signature will see. Decide on that first.
3. **A certificate profile** ("Public Trust"), and his sign-in given the role "Artifact Signing Certificate Profile Signer".
4. **On his PC:** Windows SDK's `signtool.exe` and Microsoft's signing client (the "dlib" package), or Microsoft's PowerShell module for it.

## What changes in each project

| Project | The change |
|---|---|
| **This installer** | One step in `build.ps1` after the build: `signtool sign` with the dlib and a small JSON naming the account and profile, plus a time-stamp. The exe's checksum changes, so it is printed after signing. |
| **Cadence** | Velopack signs everything (Setup.exe, Update.exe and Cadence's own files) when `vpk pack` is given the Artifact Signing options (`--azureTrustedSignFile <json>`). That is one line in `build-installer.ps1`. |

This installer needs no change for a signed Cadence: the checksums it compares are published with each release.

## What it does not do

- It does not make SmartScreen trust a file from the first download in every case. Microsoft says reputation still builds per signer. In practice signed files from this service are usually trusted quickly, but that is not promised.
- It does not cover old releases. Only files signed from then on carry a signature.

## Order, if he says yes

1. Decide on the name on the certificate.
2. Sign up and pass the identity check (his part, in the Azure portal).
3. Sign this installer first: it is the smallest change and the one file people are given.
4. Then Cadence's next release.
