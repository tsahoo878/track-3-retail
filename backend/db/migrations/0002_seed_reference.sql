-- Reference data: categories, stores, staff users
INSERT INTO categories (id, code, name, department, sort_order) VALUES
(1, 'JKT-WP', 'Waterproof Jackets', 'Clothing', 10),
(2, 'JKT-INS', 'Insulated Jackets', 'Clothing', 20),
(3, 'FLC', 'Fleeces and Midlayers', 'Clothing', 30),
(4, 'BASE', 'Base Layers', 'Clothing', 40),
(5, 'TRS', 'Walking Trousers', 'Clothing', 50),
(6, 'SHT', 'Shirts and Tops', 'Clothing', 60),
(7, 'KNIT', 'Knitwear', 'Clothing', 70),
(8, 'KID', 'Kids Outerwear', 'Clothing', 80),
(9, 'BOOT', 'Walking Boots', 'Footwear', 90),
(10, 'SHOE', 'Trail Shoes', 'Footwear', 100),
(11, 'SOCK', 'Socks', 'Accessories', 110),
(12, 'GLV', 'Gloves and Hats', 'Accessories', 120),
(13, 'PACK', 'Rucksacks', 'Equipment', 130),
(14, 'TENT', 'Tents and Shelters', 'Equipment', 140),
(15, 'SLP', 'Sleeping Bags', 'Equipment', 150),
(16, 'POLE', 'Poles and Navigation', 'Equipment', 160);

INSERT INTO stores (id, code, name, region, city) VALUES
(1, 'EDI', 'Edinburgh Grassmarket', 'Scotland', 'Edinburgh'),
(2, 'GLA', 'Glasgow Buchanan Street', 'Scotland', 'Glasgow'),
(3, 'MAN', 'Manchester Northern Quarter', 'North', 'Manchester'),
(4, 'LDS', 'Leeds Briggate', 'North', 'Leeds'),
(5, 'NCL', 'Newcastle Grey Street', 'North', 'Newcastle'),
(6, 'BHM', 'Birmingham Bullring', 'Midlands', 'Birmingham'),
(7, 'NOT', 'Nottingham Bridlesmith Gate', 'Midlands', 'Nottingham'),
(8, 'BRS', 'Bristol Cabot Circus', 'South West', 'Bristol'),
(9, 'EXE', 'Exeter High Street', 'South West', 'Exeter'),
(10, 'LCG', 'London Covent Garden', 'South East', 'London'),
(11, 'BTN', 'Brighton The Lanes', 'South East', 'Brighton'),
(12, 'CBG', 'Cambridge Market Street', 'South East', 'Cambridge');

INSERT INTO users (id, email, display_name, password_hash, role, store_id, is_active, created_utc) VALUES
(1, 'admin@kestrel-outfitters.example', 'Priya Raman', '$2a$11$kRFCfGe2paJmwGFhHeJv//iu6/gZeDnciTWHmstM/udQ1yha5VZsa', 'ADMIN', NULL, 1, '2025-03-12 09:00:00'),
(2, 'tom.achebe@kestrel-outfitters.example', 'Tom Achebe', '$2a$11$5.RIgFt959HZcPBUTnWYq8sQ3fKXjqb02NE0rh8Qg0fdYA82cWRCM', 'MANAGER', NULL, 1, '2025-03-21 09:00:00'),
(3, 'sarah.keane@kestrel-outfitters.example', 'Sarah Keane', '$2a$11$2dFUXUymUnGnPiAWUoag/11t2nt.kLbe5kqzKUNYXd8lHocDP/peo', 'MANAGER', 1, 1, '2025-03-30 09:00:00'),
(4, 'dev.patel@kestrel-outfitters.example', 'Dev Patel', '$2a$11$dI6TDKrMyA0nvxP1ObpniDqOV90uukBODCsOeXVBzJIyEXma4TcI5', 'MANAGER', 3, 1, '2025-04-08 09:00:00'),
(5, 'mairi.cole@kestrel-outfitters.example', 'Mairi Cole', '$2a$11$GvKRbXH7q41DZlWLUmNQKFyy7SUKnOadF7YMNKa1MiYBjEnivpvd.', 'STAFF', 2, 1, '2025-04-17 09:00:00'),
(6, 'josh.byrne@kestrel-outfitters.example', 'Josh Byrne', '$2a$11$eQfuUeQ79rNiIIXIa/Hf5PflhUf2Sr9.YD01tqcEN7xsIx68QZ8l4', 'STAFF', 10, 1, '2025-04-26 09:00:00'),
(7, 'ana.silva@kestrel-outfitters.example', 'Ana Silva', '$2a$11$Gtyhd6qfzv5DYa3I72hcZIA3B3UK0RQzNebhWpIXJo4xJviex08li', 'STAFF', 8, 1, '2025-05-05 09:00:00'),
(8, 'liam.ford@kestrel-outfitters.example', 'Liam Ford', '$2a$11$IvD.nWhCDrfhKpVJ30ZT3a6BavguDa5ah50G3ud6vj5syeOFPWGnk', 'BUYER', NULL, 1, '2025-05-14 09:00:00'),
(9, 'ruth.okafor@kestrel-outfitters.example', 'Ruth Okafor', '$2a$11$GWbm25INGN04Gxop45Xvrz4nb2Vyu.SNcUilxUd/7Li2nWTb1jYXq', 'BUYER', NULL, 1, '2025-05-23 09:00:00'),
(10, 'finance@kestrel-outfitters.example', 'Finance Team', '$2a$11$Omcx0UqEVHUcQZIHsjiRHs1vM6oiDwHuDKDCs957248L1B5hynbs9', 'ANALYST', NULL, 1, '2025-06-01 09:00:00');
