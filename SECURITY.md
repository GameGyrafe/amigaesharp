# Security policy

## Scope

E# is a compiler and a set of libraries for building Amiga programs. The most interesting security questions are:

- the compiler and the asset tools reading untrusted input (C# source, PNG, WAV and 8SVX files) and crashing, hanging or running out of memory;
- generated code that is unsafe when it should have been rejected (a missing bounds check, or something that should have been reported as `ES0099`);
- problems in the build scripts and Docker images (for example unpinned downloads).

Games written with E# run on hobby hardware and are out of scope.

## Supported versions

Only the current `main` branch is supported.

## Reporting

Please do not open a public issue for a vulnerability. Use GitHub's private reporting: the **Security** tab of this repository, then **Report a vulnerability**. Include what you found, how to reproduce it and what you expected. We will acknowledge it and say what we plan to do.
