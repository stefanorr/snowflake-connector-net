﻿/*
 * Copyright (c) 2012-2017 Snowflake Computing Inc. All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Snowflake.Data.Tests
{
    using NUnit.Framework;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    [SetUpFixture]
    public class SFBaseTest
    {
        private const string connectionStringFmt = "scheme=https;host={0}.snowflakecomputing.com;port=443;" +
            "user={1};password={2};account={3};role={4};db={5};schema={6};warehouse={7}";

        protected string connectionString {get; set;}

        public SFBaseTest()
        {
        }

        [OneTimeSetUp]
        public void SFTestSetup()
        {
            string testConfigString = Encoding.UTF8.GetString(Properties.Resources.parameters);

            Dictionary<string, TestConfig> testConfigs = JsonConvert.DeserializeObject<Dictionary<string, TestConfig>>(testConfigString);

            // for now hardcode to get "testconnection"
            TestConfig testConnectionConfig;
            if (testConfigs.TryGetValue("testconnection", out testConnectionConfig))
            {
                connectionString = String.Format(connectionStringFmt,
                    testConnectionConfig.account,
                    testConnectionConfig.user,
                    testConnectionConfig.password,
                    testConnectionConfig.account,
                    testConnectionConfig.role,
                    testConnectionConfig.database,
                    testConnectionConfig.schema,
                    testConnectionConfig.warehouse);
            }
            else
            {
                Assert.Fail("Failed to load test configuration");
            }
        }
    }

    class TestConfig
    {
        [JsonProperty(PropertyName = "SNOWFLAKE_TEST_USER", NullValueHandling = NullValueHandling.Ignore)]
        internal string user { get; set; }

        [JsonProperty(PropertyName = "SNOWFLAKE_TEST_PASSWORD", NullValueHandling = NullValueHandling.Ignore)]
        internal string password { get; set; }

        [JsonProperty(PropertyName = "SNOWFLAKE_TEST_ACCOUNT", NullValueHandling = NullValueHandling.Ignore)]
        internal string account { get; set; }

        [JsonProperty(PropertyName = "SNOWFLAKE_TEST_WAREHOUSE", NullValueHandling = NullValueHandling.Ignore)]
        internal string warehouse { get; set; }

        [JsonProperty(PropertyName = "SNOWFLAKE_TEST_DATABASE", NullValueHandling = NullValueHandling.Ignore)]
        internal string database { get; set; }

        [JsonProperty(PropertyName = "SNOWFLAKE_TEST_SCHEMA", NullValueHandling = NullValueHandling.Ignore)]
        internal string schema { get; set; }

        [JsonProperty(PropertyName = "SNOWFLAKE_TEST_ROLE", NullValueHandling = NullValueHandling.Ignore)]
        internal string role { get; set; }
    }
}
