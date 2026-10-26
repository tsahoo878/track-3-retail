import { NavLink, Route, Routes } from 'react-router-dom'
import ProductsPage from './pages/ProductsPage'
import OrdersPage from './pages/OrdersPage'
import OrderDetailPage from './pages/OrderDetailPage'
import StoresPage from './pages/StoresPage'
import CustomersPage from './pages/CustomersPage'
import DailySalesPage from './pages/DailySalesPage'

export default function App() {
  return (
    <div className="shell">
      <header className="topbar">
        <span className="brand">Kestrel Outfitters</span>
        <nav>
          <NavLink to="/">Orders</NavLink>
          <NavLink to="/products">Products</NavLink>
          <NavLink to="/stores">Stores</NavLink>
          <NavLink to="/customers">Customers</NavLink>
          <NavLink to="/reports/daily-sales">Daily sales</NavLink>
        </nav>
      </header>
      <main className="content">
        <Routes>
          <Route path="/" element={<OrdersPage />} />
          <Route path="/orders/:id" element={<OrderDetailPage />} />
          <Route path="/products" element={<ProductsPage />} />
          <Route path="/stores" element={<StoresPage />} />
          <Route path="/customers" element={<CustomersPage />} />
          <Route path="/reports/daily-sales" element={<DailySalesPage />} />
        </Routes>
      </main>
    </div>
  )
}
