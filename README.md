A  travel booking we application built using ASP.NET (model-view-controller) framework
that allows users to browse for trips, view trip details and book trips, pay for it and leave reviews.
Includes an Admin dashboard for managing users, trips, discounts, and a waiting list.

Features:
	USER:
				Register / Login / Forgot Password.
				View trips and trips gallery.
				Search, filter and sort trips.
				book trips and view: 
													My Bookings.
													Past Trips.
				Write reviews for booked trips.
				Profile page with user data.	
	
	ADMIN:
				Admin dashboard.
				Manage users:
										Block / Unblock.
										Add user.
										View user's booking.
				Manage trips: 
										Add Trip.
										Edit Trip.
										Manage gallery images.
				
				Discounts Management.
				Waiting list management.

  Email Messages (reminders / booking / payment / cancellations...).
  PDF itinerary generation for upcoming trips.
  
  Setup:
  			Clone the project.
  			Open the solution.
  			Configure database connection: 
  																Edit appsettings.json such that:
  																					it includes your personal connection string.
  																					it includes your Striped sandbox account for simulated payments.
  																																						
			Apply database script:
												TravelAgency_SQL_Script.txt
												
												
																						
