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

> bicep

```
@description('PostgreSQL admin password')
@secure()
param administratorLoginPassword string

param location string = resourceGroup().location
param serverName string = 'psql-ai-ragvectortest'
param administratorLogin string = 'segrepadmin'
param databaseName string = 'segrep'

resource pg 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: serverName
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    storage: {
      storageSizeGB: 32
    }
    // tighten for production
    network: {
      publicNetworkAccess: 'Enabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
  }
}

// Critical: allowlist on the server (same parameter you set with az)
resource extensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: pg
  name: 'azure.extensions'
  properties: {
    value: 'vector,pg_trgm'
    source: 'user-override'
  }
}

resource db 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: pg
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

output fqdn string = pg.properties.fullyQualifiedDomainName
output databaseName string = databaseName
```

> deploy


```
az group create -n rg-janbizub -l westeurope
az deployment group create \
  -g rg-janbizub \
  -f postgres.bicep \
  --parameters administratorLoginPassword='...'
```