using Compramas.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Stripe;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Compramas.Controllers
{
    public class ShopController : Controller
    {
        //Add db connection
        private readonly CompramasContext _context;

        //Add Configuration so Controller can read config value appsettings.json 
        private IConfiguration _configuration;

        public ShopController(CompramasContext context, IConfiguration configuration) //Dependency Injection
        { 
            //accept an instance of our DB connection class and use this object connection
            _context = context;
            //accept an instance of the configuration onjecy so we can read appsenting.json
            _configuration = configuration;
        }
        //Get: /Shop
        public IActionResult Index()
        {
            //Return ilst of Categories for the user to brows
            var categories = _context.Category.OrderBy(c => c.Name).ToList();
            return View(categories);
        }
        //Get: /Browse/catName
        public IActionResult Browse(string category)
        {
            //Store the selected category name is the ViewBag so ew can display in the view heading
            ViewBag.Category = category;
            //get the list of products for the selected category and pass the list to the view
            var products = _context.Product.Where(p => p.Category.Name == category).OrderBy(p => p.Name).ToList();
            return View(products);
        }
        //Get /ProductDetails/prodName
        public IActionResult ProductDetails(string product)
        {
            //Use a SingleOrDefault to find either 1 exact match or a null object
            var selectedproduct = _context.Product.SingleOrDefault(p => p.Name == product);
            return View(selectedproduct);
        }
        //Post /AddToCart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddToCart(int Quantity, int ProductId)
        {
            //Identity product Price
            var product = _context.Product.SingleOrDefault(p => p.ProductId == ProductId);
            var price = product.Price;
            //Determine Username
            var cartUsername = GetCartUserName();

            //Check if THIS USER'S product already exists in tthe cart. If so, update the quantity
            var cartItem = _context.Cart.SingleOrDefault(c => c.ProductId == ProductId && c.Username == cartUsername);
            if(cartItem == null)
            {
                //Create and save a new cart Object
                var cart = new Cart
                {
                    ProductId = ProductId,
                    Quantity = Quantity,
                    Price = price,
                    Username = cartUsername
                };
             _context.Cart.Add(cart);
            }
            else
            {
                cartItem.Quantity +=Quantity; //Add the new quantity to the existing quantity
                _context.Update(cartItem);
            }
            _context.SaveChanges();
            //Show to the Cart page
            return RedirectToAction("Cart");
        }
        //Check or set Cart Username
        private string GetCartUserName()
        {
            //1.Check are we already stored with Username in the User's session?
            if (HttpContext.Session.GetString("CartUserName") == null)
            {
                //Initialize an empty string variable that will later add to the Session Object
                var cartUsername = "";

                //2. If no, Username in seccion there are no item int the cart yet, is user logged in?
                //If yes, use their email for the sission variable
                if (User.Identity.IsAuthenticated)
                {
                    cartUsername = User.Identity.Name;
                }
                else
                {
                    //If no, use the GUID class to make a new ID and store that in the session
                    cartUsername = Guid.NewGuid().ToString();
                }
                //Next, store the UserName in a session var
                HttpContext.Session.SetString("CartUserName", cartUsername);
            }
            //Send back the Username
            return HttpContext.Session.GetString("CartUserName");
        }
        public IActionResult Cart()
        {
            //1. Figure who the user is
            var cartUsername = GetCartUserName();
            //2. Query the DB to get the user's cart items
            var cartItems = _context.Cart.Include(c => c.Product).Where(c => c.Username == cartUsername).ToList();
            //3. Load a view to pass the cart
            return View(cartItems);
        }
        public IActionResult RemoveFromCart(int id)
        {
            //get the object the user wants to delete
            var cartItem = _context.Cart.SingleOrDefault(c => c.CartId == id);
            //delete the object
            _context.Cart.Remove(cartItem);
            _context.SaveChanges();
            //redirect to updated cart page where deleted item is gone
            return RedirectToAction("Cart");
        }
        [Authorize]
        public IActionResult Checkout() //Set
        {
            //check if the user has been shopping anoymously now that they are logged im
            MigrateCart();
            return View();
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Checkout([Bind("FirstName, LastName, Address, City, Province, PostalCode, Phone")] Models.Order order) //Get
        {
            //autofill the date, User, and total properties instead of the user inputing these values
            order.OrderDate = DateTime.Now;
            order.UserId = User.Identity.Name;

            var cartItems = _context.Cart.Where(c => c.Username == User.Identity.Name);
            decimal cartTotal = (from c in cartItems
                                 select c.Quantity * c.Price).Sum();

            order.Total = cartTotal;
            //Will WEED and EXTENSION to the .Net Core Session object to store the order Object
            //HttpContext.Session.SetString("cartTotal", cartTotal.ToString());

            //We now have the Session to the complex object
            HttpContext.Session.SetObject("Order", order);

            return RedirectToAction("Payment");
        }
        private void MigrateCart()
        {
            //if user has shopped with out an account, attach their items to their user name
            if(HttpContext.Session.GetString("CartUsername") != User.Identity.Name)
            {
                var cartUsername = HttpContext.Session.GetString("CartUsername");
                //get the user's cart items
                var cartItems = _context.Cart.Where(c => c.Username == cartUsername);
             //loop through the cart items and update the username for each one
             foreach (var item in cartItems)
                {
                    item.Username = User.Identity.Name;
                    _context.Update(item);
                }
                _context.SaveChanges();

                //Update the session variable from a GUID to the user's email
                HttpContext.Session.SetString("CartUsername", User.Identity.Name);
            }
        }
        [Authorize]
        public IActionResult Payment() 
        {
            //Setup payment page to show order total

            //1. Get tge order from the session variable and cast as an Order Object
            var order = HttpContext.Session.GetObject<Models.Order>("Order");

            //2. Use Viewbag to display total and pass the anount to Stripe
            ViewBag.Total = order.Total;
            ViewBag.CentsTotal = order.Total * 100; //Stripe required anount in cents, not dollars and cents
            ViewBag.PublishableKey = _configuration.GetSection("Stripe")["PublishableKey"];
            return View();
        }
        //need to get w things back from Stripe after authorization
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Payment(string stripeEmail, string stripeToken)
        {
            //send payment to stripe
            StripeConfiguration.ApiKey = _configuration.GetSection("Stripe")["SecretKey"];
            var cartUsername = HttpContext.Session.GetString("CartUsername");
            var cartItems = _context.Cart.Where(c => c.Username == cartUsername);
            var order = HttpContext.Session.GetObject<Models.Order>("Order");

            //new stripe payment attempt
            var customerService = new CustomerService();
            var chargeService = new ChargeService();
            //new customer email from payment from, token auto-generated on payment form also
            var customer = customerService.Create(new CustomerCreateOptions
            {
                Email = stripeEmail,
                Source = stripeToken
            });

            //new charge using customer created above
            var charge = chargeService.Create(new ChargeCreateOptions
            {
                Amount = Convert.ToInt32(order.Total * 100),
                Description = "Compramas Puchase",
                Currency = "cad",
                Customer = customer.Id
            });

            //generate and save new order
            _context.Order.Add(order);
            _context.SaveChanges();

            //save order details
            foreach(var item in cartItems)
            {
                var orderDetail = new OrderDetail
                {
                    OrderId = order.OrderId,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    Price = item.Price
                };
                _context.OrderDetail.Add(orderDetail);
            }
            _context.SaveChanges();

            //delete the cart
            foreach(var item in cartItems)
            {
                _context.Cart.Remove(item);
            }
            _context.SaveChanges();

            //confirm with a receipt for the new OrderId

            return RedirectToAction("Details", "Orders", new {id = order.OrderId });
        }
    }
    
}
