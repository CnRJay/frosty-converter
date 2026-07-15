using Frosty.Core.Attributes;
using FrostyConvert.MmcPlugin;

// Required for Frosty/MMC PluginManager to discover and register the menu item.
[assembly: PluginDisplayName("FrostyConvert Fbmod Import")]
[assembly: PluginAuthor("FrostyConvert")]
[assembly: PluginVersion("1.0.3.0")]
[assembly: RegisterMenuExtension(typeof(ImportFbmodMenuExtension))]
