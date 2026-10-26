# Kestrel Outfitters

Kestrel Outfitters sells outdoor clothing and gear through 12 shops across the UK and a web store. Over the last
two years the back-office app that tracks products, categories, stores, stock, customers, orders and payments has
grown one request at a time. Finance relies on its daily sales report every morning, and the buyers feed it a
supplier stock file each week. Customer Operations now needs it to handle returns and refunds.

## Run it

Needs: .NET 8 SDK, Node 20, and a MySQL 8 server on port 3306 that you can reach as root.

Create the database and the app user (once):

    mysql -h 127.0.0.1 -u root -p < db-setup/01-create-users.sql

Start the API (it applies the database migrations on start-up), then the web app:

    cd backend && dotnet run --project Kestrel.Api
    cd frontend && npm install && npm run dev

Run the tests (the MySQL server must be running; the tests use the same database):

    cd backend && dotnet test

The API listens on http://localhost:5180 (Swagger at /swagger). The web app is on http://localhost:5173.

MySQL on another host or port: set the environment variable `ConnectionStrings__Kestrel` to a full connection string (server, port, database, user, password) before starting the API or the tests. The tests need MySQL.
