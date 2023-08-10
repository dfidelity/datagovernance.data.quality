import sys
from awsglue.transforms import *
from awsglue.utils import getResolvedOptions
from pyspark.context import SparkContext
from pyspark.sql.functions import lit
from awsglue.context import GlueContext
from awsglue.job import Job
from awsglue.dynamicframe import DynamicFrame
import boto3
import botocore
#import logging
import time
from datetime import datetime

## @params: [JOB_NAME]
args = getResolvedOptions(sys.argv, ['JOB_NAME','path_to_file','output_bucket_name','output_prefix','output_key','log_table_name','role_name','workers_count','timeout_value','database_suffix'])

sc = SparkContext()
glueContext = GlueContext(sc)
spark = glueContext.spark_session
job = Job(glueContext)
job.init(args['JOB_NAME'], args)

#logger = logging.getLogger('AutomateDQRulesetRecommendation')
#logger.setLevel(logging.WARNING)
client = boto3.client('glue')
db_name_list = []
database_name_list = []
table_name_list = []
ruleset_name_list = []
run_id_list = []
status_check_list = []
responseGetTables = {} 
now = datetime.now()
dt_string = now.strftime("%d_%m_%Y_%H_%M_%S")
path_to_file = args['path_to_file']
output_bucket_name = args['output_bucket_name']
output_prefix = args['output_prefix']
output_key = args['output_key']
log_table_name = args['log_table_name']
role_name=args['role_name']
number_of_workers=int(args['workers_count'])
timeout_value=int(args['timeout_value'])
exh_dashboard_db = 'exhibitor_dashboard'+args['database_suffix']
connectionbox_db = 'connectionbox'+args['database_suffix']
ratbox_db = 'ratbox'+args['database_suffix']
rx_reed_pop_db = 'rx_reed_pop'+args['database_suffix']
rx_refined_db = 'rx_refined'+args['database_suffix']
rx_reports_db = 'rx_reports'+args['database_suffix']
globaldb_raw_db = 'globaldb_raw'+args['database_suffix']
db_name_list.append(globaldb_raw_db)
db_name_list.append(exh_dashboard_db)
db_name_list.append(connectionbox_db)
db_name_list.append(ratbox_db)
db_name_list.append(rx_reed_pop_db)
db_name_list.append(rx_refined_db)
db_name_list.append(rx_reports_db)

for db_name in db_name_list:
    table_paginator = client.get_paginator('get_tables')
    table_iterator = table_paginator.paginate(DatabaseName=db_name)
    for tables in table_iterator:
        tableList = tables['TableList']
        for table_names in tableList:
            try:
                tableName = table_names['Name']
                ruleset_name = db_name+'_'+tableName+'_ruleset_'+dt_string
                response = client.start_data_quality_rule_recommendation_run( #Runid is null then error response
                    DataSource=
                    {'GlueTable': {
		            'DatabaseName': db_name,
		            'TableName': tableName
                    }
                    }   ,
                Role=role_name,
                NumberOfWorkers=number_of_workers,
                Timeout=timeout_value,
                CreatedRulesetName= ruleset_name
                )
                if response['RunId']:
                    run_id_list.append(response['RunId'])
                    table_name_list.append(tableName)
                    ruleset_name_list.append(ruleset_name)
                    database_name_list.append(db_name)
                else:
                    run_id_list.append('NA')
                    table_name_list.append(tableName)
                    ruleset_name_list.append(ruleset_name)
                    database_name_list.append(db_name)
                time.sleep(5)
            except Exception as err:
                print(err)
                run_id_list.append('NA')
                table_name_list.append(tableName)
                ruleset_name_list.append(ruleset_name)
                database_name_list.append(db_name)
for run_id in run_id_list:
    if run_id!='NA':
        status_check_response = client.get_data_quality_rule_recommendation_run(
            RunId=run_id
        )
        status_check_list.append(status_check_response['Status'])
    else:
        status_check_list.append('ERROR')
    time.sleep(5)
df = spark.createDataFrame(data = zip(database_name_list,table_name_list,ruleset_name_list,run_id_list,status_check_list), schema = ['databasename','tablename','rulesetname','runid','status'])
df = df.select('databasename','tablename','rulesetname','runid','status').withColumn("executedon", lit(now))
d_frame = DynamicFrame.fromDF(df, glueContext, 'd_frame')
d_frame_combined = d_frame.coalesce(1)

glueContext.write_dynamic_frame_from_options(
    frame=d_frame_combined,
    connection_type="dynamodb",
    connection_options={"dynamodb.output.tableName": log_table_name
    },
)

glueContext.write_dynamic_frame.from_options(
    frame=d_frame_combined,
    connection_type="s3",
    connection_options={"path": path_to_file},
    format="csv",
    format_options={
        "quoteChar": -1,
    },
)

S3client = boto3.client('s3')
try:
    S3client.delete_object(Bucket=output_bucket_name,Key=output_key)
except Exception as err:
    print(err)
response = S3client.list_objects_v2(
    Bucket=output_bucket_name,
    Prefix=output_prefix,
)
key_value = response["Contents"][0]["Key"]
S3client.copy_object(Bucket=output_bucket_name, CopySource={
    'Bucket': output_bucket_name,
    'Key': key_value
    }, Key=output_key)
S3client.delete_object(Bucket=output_bucket_name,Key=key_value)
job.commit()