# Stratis Docker Images

Here we have some basic Docker images for StraxD. The images build from the full node master on GitHub. After installing Docker, you can build and run the container with the following. 

# Build the Docker container 

```
cd Stratis.StraxD
docker build -t stratisfullnode/straxmain . 
```

# Run the Docker container
```
docker run -it <containerId>
```

# Optional

You can optionally use volumes external to the Docker container so that the blockchain does not have to sync between tests. 

## Create the volume:

```
 docker volume create stratisfullnode
```

### Run StratisD with a volume:
```
docker run --mount source=stratisfullnode,target=/root/.stratisnode -it <containerId>
```

## Optionally forward ports from your localhost to the docker image

When running the image, add a `-p <containerPort>:<localPort>` to formward the ports:

```
docker run -p 17105:17105 -it <containerId>
```

## Force rebuild of docker images from master
```
docker build . --no-cache 
```

## Run image on the MainNet rather than the TestNet. 

Modify the Dockerfile to put the conf file in the right location and remove the "-testnet" from the run statement. 

``` 
---

COPY strax.conf.docker /root/.stratisnode/strax/StraxMain/strax.conf

--- 

CMD ["dotnet", "run"]

``` 

Also remove `testnet=1` from the `*.docker.conf` file.

