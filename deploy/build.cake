#addin nuget:?package=AWSSDK.AutoScaling&version=3.3.100.7
#addin nuget:?package=AWSSDK.CloudFormation&version=3.7.105.4
#addin nuget:?package=AWSSDK.Core&version=3.7.106.18
#addin nuget:?package=AWSSDK.EC2&version=3.3.117.3
#addin nuget:?package=AWSSDK.ElasticBeanstalk&version=3.3.100.37
#addin nuget:?package=AWSSDK.S3&version=3.3.102.18
#addin nuget:?package=AWSSDK.SecurityToken&version=3.3.101.19
#addin nuget:?package=SharpZipLib&version=1.3.3
#addin nuget:?package=Firefly.CrossPlatformZip&version=0.5.0
#addin nuget:?package=YamlDotNet&version=6.0.0
#addin nuget:?package=AWSSDK.Lambda&version=3.3.102.19
#addin nuget:?package=AWSSDK.KeyManagementService&version=3.3.101.23
#addin nuget:?package=Polly&version=7.2.0

#addin nuget:?package=ReedExpo.Cake.Base&version=1.0.7
#addin nuget:?package=ReedExpo.Cake.AWS.Base&version=1.0.25
#addin nuget:?package=Cake.FileHelpers&version=3.2.0
#addin nuget:?package=ReedExpo.Cake.AWS.BuildAuthentication&version=1.0.18
#addin nuget:?package=ReedExpo.Cake.AWS.CloudFormation&version=3.7.118
#addin nuget:?package=Cake.AWS.S3&version=0.6.8
#addin nuget:?package=AWSSDK.Glue&version=3.3.106
#addin nuget:?package=AWSSDK.Athena&version=3.3.100.51
#addin nuget:?package=ReedExpo.Cake.AWS.Lambda&version=1.1.25

#addin nuget:?package=Cake.Http&version=0.6.1
#addin nuget:?package=ReedExpo.Cake.Cyclonedx&version=1.0.12
#addin nuget:?package=Newtonsoft.Json&version=13.0.1
#addin nuget:?package=ReedExpo.Cake.CrossPlatformZip&version=1.0.18
#addin nuget:?package=ReedExpo.Cake.ServiceNow&version=1.0.27
#addin nuget:?package=Firefly.EmbeddedResourceLoader&version=0.1.3

using System;
using System.Threading;
using Amazon.Lambda;
using Amazon.Runtime;
using Amazon.Glue;
using Amazon.Glue.Model;
using Amazon.Runtime.Internal;
using Amazon.Athena;
using Amazon.Athena.Model;
using Newtonsoft.Json;
using System.Collections.Generic;

AWSCredentials awsCredentials;
RegionEndpoint region = RegionEndpoint.EUWest1;
string codeBucketName;
string environment;
string deploymentRole;
string timeoutValueRecommendation;
string timeoutValueEvaluation;
string numWorkersEvaluation;
string numWorkersRecommendation;
string timeoutValueGlue;
string numWorkersGlue;
string workerTypeGlue;
string dataQalityResourcesStackName;
string changeRequestNumber;
var isRunningInBamboo = Environment.GetEnvironmentVariables().Keys.Cast<string>().Any(k => k.StartsWith("bamboo_", StringComparison.OrdinalIgnoreCase));

//////////////////////////////////////////////////////////////////////
// Arguments
//////////////////////////////////////////////////////////////////////

// DO NOT assign from bamboo environment variables here. Do it in Task("Intialise")
var target = Argument("target", "Default");
string dataQalityResourcesTemplatePath = "Data_Quality_CFN_Template.json";

// The following are assigned in Task("Intialise")
TagsInfo tagsInfo;

//////////////////////////////////////////////////////////////////////
// PRE-DEPLOY
//////////////////////////////////////////////////////////////////////

Task("Initialise")
    .Does(() => {

        // Place any initialisation, e.g. AWS authentication or  setting of variables that require environment variables to exist.
        // Having this kind of initialisation in a task permits -WhatIf runs to check task run order.

        //////////////////////////////////////////////////////////////////////
        // Authenticate with AWS.
        // See
        // https://bitbucket.org/coreshowservices/reedexpo.cake.aws.buildauthentication
        //////////////////////////////////////////////////////////////////////
        awsCredentials = GetBuildAwsCredentials();

        // Deployment environment name (set by Bamboo)
        environment = EnvironmentVariableStrict("bamboo_deploy_environment");
        deploymentRole = EnvironmentVariableStrict("bamboo_AWS_DEPLOYMENT_ROLE_ARN");
        codeBucketName = EnvironmentVariableStrict("bamboo_CODE_BUCKET_NAME");
        timeoutValueRecommendation = EnvironmentVariableStrict("bamboo_TIMEOUT_VALUE_RECOMMENDATION");
        timeoutValueEvaluation = EnvironmentVariableStrict("bamboo_TIMEOUT_VALUE_EVALUATION");
        numWorkersEvaluation = EnvironmentVariableStrict("bamboo_NUM_WORKERS_EVALUATION");
        numWorkersRecommendation = EnvironmentVariableStrict("bamboo_NUM_WORKERS_RECOMMENDATION");
        timeoutValueGlue = EnvironmentVariableStrict("bamboo_TIMEOUT_VALUE_GLUE");
        numWorkersGlue = EnvironmentVariableStrict("bamboo_NUM_WORKERS_GLUE");
        workerTypeGlue = EnvironmentVariableStrict("bamboo_WORKER_TYPE_GLUE");
        TagsInfoBuilder baseTagsBuilder = TagsInfoBuilder.Create()
            .WithEnvironmentName(environment)
            .WithFinanceEntityId("0092")
            .WithFinanceActivityId("8000")
            .WithFinanceManagementCentreId("99440")
            .WithPmProgramme("platform")
            .WithPmProjectCode("n/a")
            .WithJiraProjectCode("RDL")
            .WithServiceName("GBS Data Lake");
        TagsInfo baseTagsInfo = baseTagsBuilder;
        tagsInfo = baseTagsBuilder.WithEnvironmentName(environment);

        dataQalityResourcesStackName = $"rx-gbs-datalake-datagovernance-data-quality-{environment}";

        Information("Initialise task finished");

    });

// Task("UploadCloudFormationTemplates")
//     .IsDependentOn("Initialise")
//     .Does(async () => {
//         var templatesToUpload = new Dictionary<string, string>();
//         var files = GetFiles("**/*.json");
//         foreach(var file in files){
//             templatesToUpload.Add(file.FullPath,"data-governance/cf-templates/"+file.GetFilename());
//         }
//         await upload_to_code_bucket(awsCredentials, region, codeBucketName, templatesToUpload);
//     });


Task("DeployToCodeBucket")
    .IsDependentOn("Initialise")
    .Does(async () => {

        // Upload code bucket artifacts

        var scriptsToDeploy = new Dictionary<string, string> {
        };
        var files = GetFiles("**/*.py");
        foreach(var file in files){
           scriptsToDeploy.Add(file.FullPath,"governance/scripts/"+file.GetFilename());
         }
        await upload_to_code_bucket(awsCredentials, region, codeBucketName, scriptsToDeploy);
    });


Task("dataQualityResources")
    .IsDependentOn("DeployToCodeBucket")
    .Description("Data Quality Resources Setup")
    .WithCriteria(environment != "local")
    .Does(() => {

        var result = RunCloudFormation(
            new RunCloudFormationSettings
            {
                Credentials = awsCredentials,
                Capabilities = new List<Capability> { Capability.CAPABILITY_NAMED_IAM, Capability.CAPABILITY_AUTO_EXPAND }, // Required for creation of named service user.
                Region = region,
                StackName = dataQalityResourcesStackName,
                TemplatePath = dataQalityResourcesTemplatePath,
                Parameters = new Dictionary<string, string>
                {
                    // TODO
                    // Add additional parameters e.g. database credentials gathered from plan variables here.
                    {"BillingEnvironmentName", environment},
                    {"deploymentRole", deploymentRole},
                    {"TimeoutValueRecommendation", timeoutValueRecommendation},
                    {"TimeoutValueEvaluation", timeoutValueEvaluation},
                    {"NumWorkersEvaluation", numWorkersEvaluation},
                    {"NumWorkersRecommendation", numWorkersRecommendation},
                    {"TimeoutValueGlue", timeoutValueGlue},
                    {"NumWorkersGlue", numWorkersGlue},
                    {"WorkerTypeGlue", workerTypeGlue},
                    {"GlueJobCodeBucket", codeBucketName}
                },
                Tags = tagsInfo         // Note that applying tags to the stack will cause all resources to have the same tags when created.
            }
        );

        Information($"Stack update result was {result}");
    });


//////////////////////////////////////////////////////////////////////
// POST DEPLOYMENT
//////////////////////////////////////////////////////////////////////

Teardown(context => {

        // Teardown is EXECUTED by -WhatIf. Only permit execution when running in Bamboo
        if (isRunningInBamboo && !string.IsNullOrWhiteSpace(changeRequestNumber))
        {
            Information("Inside teardown");
            CloseChangeRequest(new ChangeRequestSettings
            {
                SystemId = changeRequestNumber,
                Success  = context.Successful,
            });
        }
 });

Task("Default")
    .IsDependentOn("dataQualityResources");

RunTarget(target);

//////////////////////////////////////////////////////////////////////
// HELPER FUNCTIONS/CLASSES
//////////////////////////////////////////////////////////////////////

string EnvironmentVariableStrict(string key)
{
    var value = EnvironmentVariable(key);

    if (value == null)
    {
        throw new Exception("Environment Variable not found: " + key);
    }

    return value;
}

string EnvironmentVariableOrDefault(string key, string defaultValue)
{
    var value = EnvironmentVariable(key);
    return value ?? defaultValue;
}

async Task upload_to_code_bucket(AWSCredentials awsCredentials, Amazon.RegionEndpoint region, string codeBucketName,
                          Dictionary<string, string> filesToUpload){
    var immutableCredentials = awsCredentials.GetCredentials();

    // https://github.com/SharpeRAD/Cake.AWS.S3
    var uploadSettings = new UploadSettings {
        AccessKey = immutableCredentials.AccessKey,
        SecretKey = immutableCredentials.SecretKey,
        SessionToken = immutableCredentials.Token,
        Region = region,
        BucketName = codeBucketName
    };

    foreach (var kv in filesToUpload)
    {
        Information($"Uploading {kv.Key} to s3://{codeBucketName}/{kv.Value}");

        await S3Upload(kv.Key, kv.Value, uploadSettings);
    }
}