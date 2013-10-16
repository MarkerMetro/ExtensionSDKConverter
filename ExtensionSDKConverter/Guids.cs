// Guids.cs
// MUST match guids.h
using System;

namespace MarkerMetro.ExtensionSDKConverter
{
    static class GuidList
    {
        public const string guidExtensionSDKConverterPkgString = "8557fba6-e4fa-42a2-9681-de6ed3fe735f";
        public const string guidExtensionSDKConverterCmdSetString = "5ed3cedd-a531-4e2d-a519-37c13452b65b";

        public static readonly Guid guidExtensionSDKConverterCmdSet = new Guid(guidExtensionSDKConverterCmdSetString);
    };
}