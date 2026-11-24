# Security policy

VRCInventoryManager reads the VRChat login cookie that VRCX saved on your PC so it can manage your stickers and emoji.

## Reporting

Please don't open a public issue. Use [a private security advisory](https://github.com/RealWhyKnot/VRCInventoryManager/security/advisories/new) so we can work out a fix before anything is published.

I try to reply within 7 days. There's no bug bounty.

## In scope

- The login cookie or any other credential being written, logged or sent anywhere it shouldn't be.
- An update being installed from a file that doesn't match what the release published.
- A crafted image or file in your screenshot folder that runs code or reads files outside it.

## Out of scope

- Anything that needs admin access to your PC already.
- Bugs in VRCX or VRChat itself.
