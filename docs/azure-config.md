# Required Extensions:

https://learn.microsoft.com/en-us/azure/postgresql/extensions/how-to-use-pgvector

https://learn.microsoft.com/en-us/azure/postgresql/extensions/how-to-allow-extensions?tabs=allow-extensions-portal#allow-extensions-in-azure-database-for-postgresql-flexible-server

https://learn.microsoft.com/en-us/azure/postgresql/extensions/concepts-extensions-considerations


### Allow extensions: PgVecor (VECTOR) + pg_trgm
```
az postgres flexible-server parameter set \
  --resource-group rg-janbizub \
  --server-name psql-ai-ragvectortest \
  --name azure.extensions \
  --value "vector,pg_trgm"
```

