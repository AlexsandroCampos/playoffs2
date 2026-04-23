# pds-back-end

Como rodar?

Ter o dotnet 7 instalado

Ter docker desktop instalado

No terminal powershell em que vai rodar a aplicação definir as variáveis de ambiente
$env:EMAILCLIENT="xxx"
$env:EMAILPASSWORD="xxx"
$env:IS_DEVELOPMENT="true"
$env:ASPNETCORE_ENVIRONMENT="Development"
$env:MOUNT_PATH="C:/playoffs/uploads" -> caminho de uma pasta que vc precisa criar
$env:CAPTCHA_KEY="SUA_CHAVE_RECAPTCHA"
$env:SUPER_SECRET_PASSWORD="SENHA_ADMIN_LOCAL"

Subir PostgreSQL, Redis e Elasticsearch com Docker:
No PowerShell, execute:

docker run -d --name playoffs-postgres -e POSTGRES_PASSWORD=123456 -e POSTGRES_DB=postgres -p 5432:5432 postgres:15

docker run -d --name playoffs-redis -p 6379:6379 redis:7

docker run -d --name playoffs-elastic -p 9200:9200 -e discovery.type=single-node -e xpack.security.enabled=false -e ES_JAVA_OPTS=-Xms512m -e ES_JAVA_OPTS=-Xmx512m docker.elastic.co/elasticsearch/elasticsearch:8.9.0


Criar banco de dados e tabelas com docker
docker exec -it playoffs-postgres psql -U postgres -d postgres

CREATE DATABASE playoffs;
\q

embaixo vc precisa colocar o caminho para chegar no arquivo schema.sql
docker cp C:\Users\Alex\Documents\faculdade\semestre_5\bd2\back\schema.sql playoffs-postgres:/tmp/schema.sql

docker exec -it playoffs-postgres psql -U postgres -d playoffs -f /tmp/schema.sql



