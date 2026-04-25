using 'main.bicep'

param environment = 'prod'
param sqlAdminLogin = 'tracerAdmin'
// sqlAdminPassword must be provided at deployment time via --parameters or Key Vault
