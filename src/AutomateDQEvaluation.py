import sys
from awsglue.transforms import *
from awsglue.utils import getResolvedOptions
from pyspark.sql.functions import lit
from pyspark.context import SparkContext
from awsglue.context import GlueContext
from awsglue.job import Job
import boto3
import time
from datetime import datetime
from awsglue.dynamicframe import DynamicFrame

## @params: [JOB_NAME]
args = getResolvedOptions(sys.argv, ['JOB_NAME','read_from_file','result_prefix_s3','output_bucket_name','log_table_name','role_name','workers_count','timeout_value'])

sc = SparkContext()
glueContext = GlueContext(sc)
spark = glueContext.spark_session
job = Job(glueContext)
job.init(args['JOB_NAME'], args)
evaluation_run_id_list = []
evaluation_run_status = []
table_name_list = []
database_name_list = []
ruleset_name_list = []
now = datetime.now()
dt_string = now.strftime("%d_%m_%Y_%H_%M_%S")
read_from_file = args['read_from_file']
result_prefix_s3 = args['result_prefix_s3']
output_bucket_name = args['output_bucket_name']
log_table_name = args['log_table_name']
role_name= args['role_name']
number_of_workers=int(args['workers_count'])
timeout_value=int(args['timeout_value'])

client = boto3.client('glue')
dynamicFrame = glueContext.create_dynamic_frame.from_options(
    connection_type="s3",
    connection_options={"paths": [read_from_file]},
    format="csv",
    format_options={
        "withHeader": True,
    },
)
df = dynamicFrame.toDF()
ruleset_map = df.collect()
for r_name in ruleset_map:
    if r_name['status']!='FAILED':
        if r_name['status']!='NA':
            print('Started for ',r_name['tablename'])
            try:
                response = client.start_data_quality_ruleset_evaluation_run(
                    DataSource={
                        'GlueTable': {
                            'DatabaseName': r_name['databasename'],
                            'TableName': r_name['tablename']
                        }
                    
                    },
                Role=role_name,
                NumberOfWorkers=number_of_workers,
                Timeout=timeout_value,
                AdditionalRunOptions={
                'CloudWatchMetricsEnabled': True,
                'ResultsS3Prefix': result_prefix_s3
                },
                RulesetNames=[r_name['rulesetname']]
                )
                table_name_list.append(r_name['tablename'])
                database_name_list.append(r_name['databasename'])
                ruleset_name_list.append(r_name['rulesetname'])
                evaluation_run_id_list.append(response['RunId'])
            except Exception as err:
                print(err)
            time.sleep(5)
        else:
            table_name_list.append(r_name['tablename'])
            database_name_list.append(r_name['databasename'])
            ruleset_name_list.append('NA')
            evaluation_run_id_list.append('NA')
    else:
        table_name_list.append(r_name['tablename'])
        database_name_list.append(r_name['databasename'])
        ruleset_name_list.append('NA')
        evaluation_run_id_list.append('NA')
        
for run_id in evaluation_run_id_list:
    if run_id!='NA':
        status_check_Response = client.get_data_quality_ruleset_evaluation_run(
            RunId=run_id
        )
        evaluation_run_status.append(status_check_Response['Status'])
        time.sleep(5)
    else:
        evaluation_run_status.append('NA')
df = spark.createDataFrame(data = zip(database_name_list,table_name_list,ruleset_name_list,evaluation_run_id_list,evaluation_run_status), schema = ['databasename','tablename','rulesetname','runid','evaluationrunstatus'])
df = df.select('databasename','tablename','rulesetname','runid','evaluationrunstatus').withColumn("executedon", lit(now))
d_frame = DynamicFrame.fromDF(df, glueContext, "d_frame")
d_frame_combined = d_frame.coalesce(1)

glueContext.write_dynamic_frame_from_options(
    frame=d_frame_combined,
    connection_type="dynamodb",
    connection_options={"dynamodb.output.tableName": log_table_name,
    },
)

job.commit()
