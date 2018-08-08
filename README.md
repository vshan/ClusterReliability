---
services: service-fabric
platforms: dotnet
---

# Cluster Reliability
The cluster reliability application makes the application in secondary cluster data ready by periodically restoring latest backups available from primary cluster.

##Components
This application has 3 components :
 - WebInterface( stateless frontend service)
 - Restore Service (statefu) : Stores partition mappings and triggers restore periodically
 - PolicyStorage Service (stateful) : Stores storage details per policy 

## How to use
Clone this repo and build and deploy this application. Then after the application had started, open webbrowser and goto (localhost:8787) where you can see the application landing page.
