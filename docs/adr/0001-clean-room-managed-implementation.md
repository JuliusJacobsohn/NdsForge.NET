# Use a clean-room managed implementation

NdsForge.NET is implemented independently from public format documentation and black-box observations because the established `ndstool` implementation is GPL-licensed while this reusable library is MIT-licensed. GPL tools may act as optional development oracles, but production code neither links to them nor translates their source; this preserves a dependency-free managed runtime and a clear licensing boundary at the cost of maintaining our own compatibility tests.

