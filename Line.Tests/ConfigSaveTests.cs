using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Xunit;

namespace Line.Tests
{
    public class ConfigSaveTests
    {
        private static void InvokeSave(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            method!.Invoke(instance, null);
        }

        private static string GetConfigPath(object instance)
        {
            var field = instance.GetType().GetField("configPath", BindingFlags.NonPublic | BindingFlags.Instance);
            return (string)field!.GetValue(instance)!;
        }

        private static void TestSaveConfig(Type type)
        {
            var instance = FormatterServices.GetUninitializedObject(type);
            var path = GetConfigPath(instance);
            var dir = Path.GetDirectoryName(path)!;
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }

            InvokeSave(instance, "SaveConfig");

            Assert.True(File.Exists(path));
        }

        [Fact]
        public void LineForm_SaveConfig_CreatesFile()
        {
            TestSaveConfig(typeof(Line.LineForm));
        }

        [Fact]
        public void VerticalLineForm_SaveConfig_CreatesFile()
        {
            TestSaveConfig(typeof(Line.VerticalLineForm));
        }

        [Fact]
        public void HorizontalLineForm_SaveConfig_CreatesFile()
        {
            TestSaveConfig(typeof(Line.HorizontalLineForm));
        }

        [Fact]
        public void BoundingBoxForm_SaveConfig_CreatesFile()
        {
            TestSaveConfig(typeof(Line.BoundingBoxForm));
        }
    }
}
