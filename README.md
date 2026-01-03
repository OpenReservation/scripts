# scripts

## Intro

Scripts for build

## bash scripts for services deployments

```sh
# identity-server
kubectl apply -f https://github.com/OpenReservation/Identity/raw/refs/heads/dev/k8s/id-server.yaml
# redis
kubectl apply -f https://github.com/OpenReservation/OpenReservation/raw/refs/heads/dev/k8s/redis.yaml
# reservation-app
kubectl apply -f https://github.com/OpenReservation/OpenReservation/raw/refs/heads/dev/k8s/reservation-deployment.yaml
# reservation-client
kubectl apply -f https://github.com/OpenReservation/reservation-angular-client/raw/refs/heads/main/k8s-deploy.yaml
# notification
kubectl apply -f https://github.com/OpenReservation/OpenReservation.Notification/raw/refs/heads/main/k8s/manifests/deployment.yaml
# schedule-service
kubectl apply -f https://github.com/OpenReservation/OpenReservation.ScheduleServices/blob/main/k8s/manifests/deployment.yaml
# sparktodo
# kubectl apply -f https://github.com/WeihanLi/SparkTodo/raw/refs/heads/main/sparktodo-api-k8s-deploy.yaml
```
