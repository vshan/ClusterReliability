---
services: service-fabric
platforms: dotnet
author: t-arsaic
---

# StandByApplication
The stand-by application makes the application in secondary cluster data ready by periodically restoring latest backups available from primary cluster.

##Components
This application has 3 components :
 - WebInterface( stateless frontend service)
 - Restore Service (statefu) : Stores partition mappings and triggers restore periodically
 - PolicyStorage Service (stateful) : Stores storage details per policy 

## How to use
  1. Clone this repo and build and deploy this application. Then after the application had started, open webbrowser and goto (localhost:8787) where you can see the application landing page.
  2. In configure tab you can enter primary and secondary cluster details and then select applications to configure for the standby.
  3. Enter policy storage details as per the policy and configure.
  4. After configuring you can view the partition restore status in status page which will automatically reloaded every 10 seconds.
