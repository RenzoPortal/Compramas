using Compramas.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Compramas.Controllers
{
    public class HomeController : Controller
    {
        //Add db connection
        private readonly CompramasContext _context;

        public HomeController(CompramasContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var compramasContext = _context.Product.Include(p => p.Category);
            return View(await compramasContext.ToListAsync());
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Contact()
        {
            return View();
        }
        public IActionResult About()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
